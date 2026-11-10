# Copyright (c) 2026 Muhammad Waleed Badar
# SPDX-License-Identifier: Apache-2.0

zephyr_library()
zephyr_library_sources(rpi_pico_system_timer.c)
# For system_timer_generic.h, the timer core that the drivers in Zephyr share.
zephyr_library_include_directories(${ZEPHYR_BASE}/drivers/timer)
