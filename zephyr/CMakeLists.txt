# Copyright (c) 2026 Muhammad Waleed Badar
# SPDX-License-Identifier: Apache-2.0

add_subdirectory_ifdef(CONFIG_RP2350_SMP
  ${ZEPHYR_CURRENT_MODULE_DIR}/soc/rp2350 ${CMAKE_CURRENT_BINARY_DIR}/soc/rp2350)

# The boot ROM stand-in that Renode loads, built next to every Arm image for
# the RP2350.
if(CONFIG_SOC_SERIES_RP2350 AND CONFIG_ARM)
  include(${ZEPHYR_CURRENT_MODULE_DIR}/cmake/bare_metal.cmake)

  set(bootrom_dir ${ZEPHYR_CURRENT_MODULE_DIR}/bootrom)

  rp2350_bare_metal_elf(rp2350_bootrom
    OUTPUT ${ZEPHYR_BINARY_DIR}/rp2350_bootrom.elf
    LINKER_SCRIPT ${bootrom_dir}/bootrom.ld
    SOURCES ${bootrom_dir}/start.S ${bootrom_dir}/bootrom.c
    DEPENDS ${bootrom_dir}/bootrom.h
  )
endif()
