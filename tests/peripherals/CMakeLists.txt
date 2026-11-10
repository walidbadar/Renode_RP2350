# Copyright (c) 2026 Muhammad Waleed Badar
# SPDX-License-Identifier: Apache-2.0

# The image is only what twister needs to have built: the tests in
# peripherals.robot load no image, just the boot ROM stand-in that the board
# builds next to it.

cmake_minimum_required(VERSION 3.20.0)

find_package(Zephyr REQUIRED HINTS $ENV{ZEPHYR_BASE})
project(peripherals)

target_sources(app PRIVATE src/main.c)
