// SPDX-License-Identifier: MPL-2.0

#pragma once
#ifdef __cplusplus
#include <bit>
#define __builtin_ctz(value) std::countr_zero(static_cast<unsigned int>(value))
#define __builtin_ctzll(value) std::countr_zero(static_cast<unsigned long long>(value))
#define __builtin_clzll(value) std::countl_zero(static_cast<unsigned long long>(value))
#endif
#define __attribute__(attributes)
#define __attribute(attributes)
#define __builtin_unreachable() __assume(0)
