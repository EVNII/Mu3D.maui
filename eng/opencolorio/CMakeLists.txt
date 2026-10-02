cmake_minimum_required(VERSION 3.20)
project(Mu3DOpenColorIO VERSION 1.0 LANGUAGES C CXX)
if(NOT DEFINED MU3D_OCIO_SOURCE)
  message(FATAL_ERROR "Use the maintainer build.py script with pinned source archives.")
endif()
set(CMAKE_CXX_STANDARD 17 CACHE STRING "C++ language standard" FORCE)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(OCIO_INSTALL_EXT_PACKAGES ALL CACHE STRING "" FORCE)
# The first host runtime uses scalar FP32 CPU evaluation, avoiding architecture-specific SIMD
# downloads and preserving the same independently checked operation path on every maintained build.
foreach(feature SSE2 SSE3 SSSE3 SSE4 SSE42 AVX AVX2 AVX512 F16C NEON SSE2NEON)
  set(OCIO_USE_${feature} OFF CACHE BOOL "" FORCE)
endforeach()
foreach(feature APPS TESTS GPU_TESTS DOCS PYTHON JAVA OPENFX NUKE)
  set(OCIO_BUILD_${feature} OFF CACHE BOOL "" FORCE)
endforeach()
add_subdirectory("${MU3D_OCIO_SOURCE}" ocio)
if(MU3D_OCIO_TARGET STREQUAL "maccatalyst-arm64")
  # CPU configuration processing does not enumerate system monitors. The upstream headless
  # implementation is portable, while macOS ColorSync/IOKit monitor APIs are unavailable in
  # Catalyst. Remove only those unused monitor frameworks from both static link interfaces.
  target_compile_definitions(OpenColorIO PRIVATE OCIO_HEADLESS_ENABLED)
  foreach(property LINK_LIBRARIES INTERFACE_LINK_LIBRARIES)
    get_target_property(libraries OpenColorIO ${property})
    list(FILTER libraries EXCLUDE REGEX "-framework (ColorSync|CoreFoundation|CoreGraphics|IOKit)")
    set_property(TARGET OpenColorIO PROPERTY ${property} "${libraries}")
  endforeach()
  add_library(mu3d_opencolorio STATIC mu3d_opencolorio.cpp)
else()
  add_library(mu3d_opencolorio SHARED mu3d_opencolorio.cpp)
endif()
target_link_libraries(mu3d_opencolorio PRIVATE OpenColorIO)
target_compile_definitions(mu3d_opencolorio PRIVATE MU3D_OCIO_EXPORTS)
if(APPLE AND NOT MU3D_OCIO_TARGET STREQUAL "maccatalyst-arm64")
  target_link_options(mu3d_opencolorio PRIVATE "-Wl,-exported_symbols_list,${CMAKE_CURRENT_SOURCE_DIR}/exports.txt")
endif()
set_target_properties(mu3d_opencolorio PROPERTIES VERSION 1.0 SOVERSION 1)
add_executable(mu3d_ocio_oracle oracle.cpp)
target_link_libraries(mu3d_ocio_oracle PRIVATE OpenColorIO)
if(MU3D_OCIO_TARGET STREQUAL "maccatalyst-arm64")
  # A .NET Apple NativeReference needs one archive containing the complete static dependency
  # closure. None of these inputs are host libraries: every ExternalProject uses the toolchain.
  set(archive "${CMAKE_BINARY_DIR}/runtimes/maccatalyst-arm64/native/libmu3d_opencolorio.a")
  add_custom_command(OUTPUT "${archive}"
    COMMAND ${CMAKE_COMMAND} -E make_directory "${CMAKE_BINARY_DIR}/runtimes/maccatalyst-arm64/native"
    COMMAND /usr/bin/libtool -static -o "${archive}"
      $<TARGET_FILE:mu3d_opencolorio> $<TARGET_FILE:OpenColorIO>
      $<TARGET_FILE:expat::expat> $<TARGET_FILE:Imath::Imath>
      $<TARGET_FILE:pystring::pystring> $<TARGET_FILE:yaml-cpp::yaml-cpp>
      $<TARGET_FILE:MINIZIP::minizip-ng> $<TARGET_FILE:ZLIB::ZLIB>
    DEPENDS mu3d_opencolorio OpenColorIO)
  add_custom_target(mu3d_opencolorio_apple_archive DEPENDS "${archive}")
  # Link the actual combined archive, independently of CMake's transitive dependency graph.
  add_executable(mu3d_ocio_native_smoke native_smoke.cpp)
  add_dependencies(mu3d_ocio_native_smoke mu3d_opencolorio_apple_archive)
  target_link_libraries(mu3d_ocio_native_smoke PRIVATE "${archive}")
endif()
install(TARGETS mu3d_opencolorio RUNTIME DESTINATION bin LIBRARY DESTINATION lib ARCHIVE DESTINATION lib)
