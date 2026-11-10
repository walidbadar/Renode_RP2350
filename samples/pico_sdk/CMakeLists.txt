# Copyright (c) 2026 Muhammad Waleed Badar
# SPDX-License-Identifier: GPL-3.0-only
#
# The dual-core checks through the Raspberry Pi Pico SDK. PICO_SDK_PATH has
# to be set in the environment; the copy of the SDK that a Zephyr workspace
# keeps in modules/hal/rpi_pico will do.

cmake_minimum_required(VERSION 3.13)

include($ENV{PICO_SDK_PATH}/external/pico_sdk_import.cmake)

project(sdk_multicore C CXX ASM)

pico_sdk_init()

add_executable(sdk_multicore main.c)
target_link_libraries(sdk_multicore pico_stdlib pico_multicore)

pico_enable_stdio_uart(sdk_multicore 1)
pico_enable_stdio_usb(sdk_multicore 0)
