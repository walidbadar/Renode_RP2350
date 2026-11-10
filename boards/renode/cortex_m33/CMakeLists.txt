# Copyright (c) 2026 Muhammad Waleed Badar
# SPDX-License-Identifier: Apache-2.0

# The boot ROM stand-in, built next to the image as rp2350_bootrom.elf.
# Renode needs it for either core to start, and scripts/rp2350.resc looks
# for it beside the image it is given.
include(${CMAKE_CURRENT_LIST_DIR}/../../../cmake/bare_metal.cmake)

set(bootrom_dir ${CMAKE_CURRENT_LIST_DIR}/../../../bootrom)

rp2350_bare_metal_elf(rp2350_bootrom
  OUTPUT ${PROJECT_BINARY_DIR}/rp2350_bootrom.elf
  LINKER_SCRIPT ${bootrom_dir}/bootrom.ld
  SOURCES ${bootrom_dir}/start.S ${bootrom_dir}/bootrom.c
  DEPENDS ${bootrom_dir}/bootrom.h
)
