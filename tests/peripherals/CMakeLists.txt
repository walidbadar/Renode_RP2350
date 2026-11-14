# Copyright (c) 2026 Muhammad Waleed Badar
# SPDX-License-Identifier: Apache-2.0

# peripherals.robot loads no image, only the boot ROM stand-in that is built
# next to it. The Zephyr image is only there for twister.

cmake_minimum_required(VERSION 3.20.0)

find_package(Zephyr REQUIRED HINTS $ENV{ZEPHYR_BASE})
project(peripherals)

target_sources(app PRIVATE src/main.c)
