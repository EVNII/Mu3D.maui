// Mu3D-owned stable C ABI over the pinned, privately linked OpenColorIO C++ implementation.
#include <OpenColorIO/OpenColorIO.h>
#include <OpenColorIO/OpenColorTransforms.h>
#include <cstdint>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

#if defined(_WIN32)
#define MU3D_API extern "C" __declspec(dllexport)
#else
#define MU3D_API extern "C" __attribute__((visibility("default")))
#endif
namespace OCIO = OCIO_NAMESPACE;
namespace {
thread_local std::string error;
struct Configuration { OCIO::ConstConfigRcPtr value; OCIO::ConstContextRcPtr context; };
struct Processor { OCIO::ConstCPUProcessorRcPtr value; };
template<class F> int checked(F&& action) noexcept {
    try { error.clear(); action(); return 0; }
    catch (const std::exception& ex) { error = ex.what(); return -1; }
    catch (...) { error = "Unknown native OpenColorIO failure."; return -1; }
}
void required(const char* value, const char* field) {
    if (!value || !*value) throw std::invalid_argument(std::string(field) + " must be explicit and nonempty.");
}
std::vector<std::string> names(Configuration* config, int kind, const char* display) {
    std::vector<std::string> result;
    auto c = config->value;
    if (kind == 0) for(int i=0;i<c->getNumColorSpaces(OCIO::SEARCH_REFERENCE_SPACE_ALL, OCIO::COLORSPACE_ALL);++i)
        result.emplace_back(c->getColorSpaceNameByIndex(OCIO::SEARCH_REFERENCE_SPACE_ALL, OCIO::COLORSPACE_ALL,i));
    else if (kind == 1) for(int i=0;i<c->getNumDisplaysAll();++i) result.emplace_back(c->getDisplayAll(i));
    else if (kind == 2) for(int i=0;i<c->getNumLooks();++i) result.emplace_back(c->getLookNameByIndex(i));
    else if (kind == 3) {
        required(display,"display");
        for (auto type : { OCIO::VIEW_DISPLAY_DEFINED, OCIO::VIEW_SHARED })
            for(int i=0;i<c->getNumViews(type,display);++i) result.emplace_back(c->getView(type,display,i));
    } else throw std::invalid_argument("Invalid enumeration kind.");
    return result;
}
std::string actual_output(const Configuration* c,const char* display,const char* view) {
    if (!c->value->hasView(display,view)) throw std::invalid_argument("Unknown display/view pair.");
    std::string result = c->value->getDisplayViewColorSpaceName(display,view);
    if (result == OCIO::OCIO_VIEW_USE_DISPLAY_NAME) result=display;
    return result;
}
}
MU3D_API uint32_t mu3d_ocio_abi_version() noexcept { return 1; }
MU3D_API const char* mu3d_ocio_version() noexcept { return OCIO::GetVersion(); }
MU3D_API const char* mu3d_ocio_last_error() noexcept { return error.c_str(); }
MU3D_API int mu3d_ocio_config_create(int kind,const char* value,const char* working_directory,void** output) noexcept {
    return checked([&] {
        required(value,"configuration"); *output=nullptr;
        OCIO::ConstConfigRcPtr c;
        if(kind==0) c=OCIO::Config::CreateFromFile(value);
        else if(kind==1) { std::istringstream stream(value); c=OCIO::Config::CreateFromStream(stream); }
        else if(kind==2) c=OCIO::Config::CreateFromBuiltinConfig(value);
        else throw std::invalid_argument("Invalid configuration source.");
        if(c->getMajorVersion()!=2) throw std::invalid_argument("Mu3D requires an explicit OCIO v2 configuration.");
        c->validate();
        auto context=c->getCurrentContext()->createEditableCopy();
        // Config parsing may initialize its private context from process variables. Replace those
        // with only declared defaults; never load OCIO or context values from the process environment.
        context->clearStringVars();
        for(int i=0;i<c->getNumEnvironmentVars();++i) {
            const char* name=c->getEnvironmentVarNameByIndex(i);
            context->setStringVar(name,c->getEnvironmentVarDefault(name));
        }
        context->setEnvironmentMode(OCIO::ENV_ENVIRONMENT_LOAD_PREDEFINED);
        if(working_directory && *working_directory) context->setWorkingDir(working_directory);
        *output=new Configuration{c,context};
    });
}
MU3D_API void mu3d_ocio_config_delete(Configuration* value) noexcept { delete value; }
MU3D_API int mu3d_ocio_config_count(Configuration* c,int kind,const char* display,int* count) noexcept {
    return checked([&] { *count=kind==4 ? static_cast<int>(OCIO::BuiltinConfigRegistry::Get().getNumBuiltinConfigs()) : static_cast<int>(names(c,kind,display).size()); });
}
// String results use caller-owned storage via a size query, avoiding pointers into disposed objects.
MU3D_API int mu3d_ocio_config_string(Configuration* c,int kind,const char* display,const char* view,int index,char* output,int capacity,int* length) noexcept {
    return checked([&] {
        std::string value;
        if(kind==6) value=OCIO::BuiltinConfigRegistry::Get().getBuiltinConfigName(static_cast<size_t>(index));
        else if(kind==4) value=c->value->getCacheID(c->context);
        else if(kind==5) value=actual_output(c,display,view);
        else { auto list=names(c,kind,display); value=list.at(static_cast<size_t>(index)); }
        *length=static_cast<int>(value.size())+1;
        if(output) { if(capacity<*length) throw std::invalid_argument("String buffer is too small."); value.copy(output,value.size()); output[value.size()]=0; }
    });
}
MU3D_API int mu3d_ocio_processor_colorspace(Configuration* c,const char* source,const char* destination,void** output) noexcept {
    return checked([&] {
        required(source,"source"); required(destination,"destination"); *output=nullptr;
        auto processor=c->value->getProcessor(c->context,source,destination);
        *output=new Processor{processor->getOptimizedCPUProcessor(OCIO::OPTIMIZATION_LOSSLESS)};
    });
}
MU3D_API int mu3d_ocio_processor_display(Configuration* c,const char* source,const char* display,const char* view,
    const char* looks,const char* decode_source,const char* linear_destination,void** output) noexcept {
    return checked([&] {
        required(source,"source"); required(display,"display"); required(view,"view"); *output=nullptr;
        auto group=OCIO::GroupTransform::Create();
        auto transform=OCIO::DisplayViewTransform::Create();
        transform->setSrc(source); transform->setDisplay(display); transform->setView(view);
        if(looks) {
            transform->setLooksBypass(true);
            if(*looks) {
                auto look=OCIO::LookTransform::Create(); look->setSrc(source); look->setDst(source); look->setLooks(looks);
                group->appendTransform(look);
            }
        }
        group->appendTransform(transform);
        if(linear_destination) {
            required(decode_source,"display output colorspace"); required(linear_destination,"linear destination");
            auto actual=c->value->getColorSpace(actual_output(c,display,view).c_str());
            auto encoded=c->value->getColorSpace(decode_source);
            auto linear=c->value->getColorSpace(linear_destination);
            if(!actual || !encoded || !linear || std::string(actual->getName())!=encoded->getName())
                throw std::invalid_argument("The declared display output must match the view's actual output colorspace.");
            if(encoded->getReferenceSpaceType()!=linear->getReferenceSpaceType())
                throw std::invalid_argument("Display decoding cannot cross OCIO scene/display reference spaces and implicitly invert a view transform.");
            auto decode=OCIO::ColorSpaceTransform::Create(); decode->setSrc(decode_source); decode->setDst(linear_destination);
            group->appendTransform(decode);
        }
        auto processor=c->value->getProcessor(c->context,group,OCIO::TRANSFORM_DIR_FORWARD);
        *output=new Processor{processor->getOptimizedCPUProcessor(OCIO::OPTIMIZATION_LOSSLESS)};
    });
}
MU3D_API void mu3d_ocio_processor_delete(Processor* value) noexcept { delete value; }
MU3D_API int mu3d_ocio_processor_cache_id(Processor* p,char* output,int capacity,int* length) noexcept {
    return checked([&] {
        std::string value=p->value->getCacheID(); *length=static_cast<int>(value.size())+1;
        if(output) { if(capacity<*length) throw std::invalid_argument("String buffer is too small."); value.copy(output,value.size()); output[value.size()]=0; }
    });
}
MU3D_API int mu3d_ocio_apply(Processor* p,float* pixels,int count,int stride) noexcept {
    return checked([&] {
        if(count<0 || (stride!=3 && stride!=4) || (!pixels && count)) throw std::invalid_argument("Invalid RGB pixel buffer.");
        for(int i=0;i<count;++i) p->value->applyRGB(pixels+static_cast<size_t>(i)*stride);
    });
}
