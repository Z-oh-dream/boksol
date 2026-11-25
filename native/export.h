#pragma once

#ifdef _WIN32
#define GDS extern "C" __declspec(dllexport)
#else
#define GDS extern "C" __attribute__((visibility("default")))
#endif
