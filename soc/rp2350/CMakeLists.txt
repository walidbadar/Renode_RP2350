# Copyright (c) 2026 Muhammad Waleed Badar
# SPDX-License-Identifier: Apache-2.0

zephyr_library()
zephyr_library_sources(smp.c)
# For ipi.h, where the kernel declares what an IPI handler has to call.
zephyr_library_include_directories(${ZEPHYR_BASE}/kernel/include)
