# Copyright (c) 2026 Muhammad Waleed Badar
# SPDX-License-Identifier: Apache-2.0

# Builds the bare-metal sample in samples/multicore, which is no Zephyr
# application, next to an image that is only what twister needs to have
# built. The tests in multicore.robot run the sample, as multicore.elf, and
# not the image.

cmake_minimum_required(VERSION 3.20.0)

find_package(Zephyr REQUIRED HINTS $ENV{ZEPHYR_BASE})
project(multicore)

target_sources(app PRIVATE src/main.c)

include(${CMAKE_CURRENT_LIST_DIR}/../../cmake/bare_metal.cmake)

set(sample_dir ${CMAKE_CURRENT_LIST_DIR}/../../samples/multicore)

rp2350_bare_metal_elf(multicore
  OUTPUT ${ZEPHYR_BINARY_DIR}/multicore.elf
  LINKER_SCRIPT ${sample_dir}/multicore.ld
  SOURCES ${sample_dir}/startup.S ${sample_dir}/main.c
  DEPENDS ${sample_dir}/rp2350.h
)
