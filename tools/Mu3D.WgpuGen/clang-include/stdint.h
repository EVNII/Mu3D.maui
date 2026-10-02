#ifndef MU3D_GENERATOR_STDINT_H
#define MU3D_GENERATOR_STDINT_H

typedef __INT8_TYPE__ int8_t;
typedef __UINT8_TYPE__ uint8_t;
typedef __INT16_TYPE__ int16_t;
typedef __UINT16_TYPE__ uint16_t;
typedef __INT32_TYPE__ int32_t;
typedef __UINT32_TYPE__ uint32_t;
typedef __INT64_TYPE__ int64_t;
typedef __UINT64_TYPE__ uint64_t;

#define UINT32_C(value) __UINT32_C(value)
#define UINT64_C(value) __UINT64_C(value)
#define UINT32_MAX __UINT32_MAX__
#define UINT64_MAX __UINT64_MAX__

#endif
