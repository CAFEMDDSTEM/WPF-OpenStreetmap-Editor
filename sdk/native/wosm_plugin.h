#ifndef WOSM_PLUGIN_H
#define WOSM_PLUGIN_H

#include <stdint.h>

#define WOSM_PLUGIN_ABI_VERSION 1

#if defined(_WIN32)
#define WOSM_PLUGIN_EXPORT __declspec(dllexport)
#define WOSM_PLUGIN_CALL __cdecl
#else
#define WOSM_PLUGIN_EXPORT __attribute__((visibility("default")))
#define WOSM_PLUGIN_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

WOSM_PLUGIN_EXPORT int WOSM_PLUGIN_CALL wosm_plugin_abi_version(void);

WOSM_PLUGIN_EXPORT char* WOSM_PLUGIN_CALL wosm_plugin_invoke(
    const uint8_t* request_utf8,
    int32_t request_length);

WOSM_PLUGIN_EXPORT void WOSM_PLUGIN_CALL wosm_plugin_free(char* response_utf8);

#ifdef __cplusplus
}
#endif

#endif
