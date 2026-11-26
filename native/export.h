// SPDX-License-Identifier: MPL-2.0

#pragma once

#ifdef _WIN32
#define GDS extern "C" __declspec(dllexport)
#else
#define GDS extern "C" __attribute__((visibility("default")))
#endif
