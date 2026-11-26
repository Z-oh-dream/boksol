// SPDX-License-Identifier: MPL-2.0

#pragma once
#ifndef _WIN32
#include_next <dirent.h>
#endif
// The supported Windows build uses std::filesystem for directory traversal.
