# Copyright (c) 2026 Muhammad Waleed Badar
# SPDX-License-Identifier: Apache-2.0

# rp2350_bare_metal_elf(<target>
#                       OUTPUT <file.elf>
#                       LINKER_SCRIPT <file.ld>
#                       SOURCES <file>...
#                       [DEPENDS <file>...])
#
# Builds a program that is not a Zephyr image with the compiler of the Zephyr
# build it is called from, and makes that image depend on it.
function(rp2350_bare_metal_elf target)
  cmake_parse_arguments(BM "" "OUTPUT;LINKER_SCRIPT" "SOURCES;DEPENDS" ${ARGN})

  add_custom_command(
    OUTPUT ${BM_OUTPUT}
    COMMAND ${CMAKE_C_COMPILER}
      -mcpu=cortex-m33 -mthumb -mfloat-abi=soft
      -Os -g -ffreestanding -fno-builtin -ffunction-sections -fdata-sections
      -Wall -Wextra -Werror
      -nostdlib -Wl,--gc-sections -Wl,--no-warn-rwx-segments
      -T ${BM_LINKER_SCRIPT}
      -o ${BM_OUTPUT}
      ${BM_SOURCES}
    DEPENDS ${BM_SOURCES} ${BM_LINKER_SCRIPT} ${BM_DEPENDS}
    COMMENT "Building ${target}"
    VERBATIM
  )
  add_custom_target(${target} DEPENDS ${BM_OUTPUT})

  # "west build -t run" builds the image and not "all", so this has to be
  # something the image depends on.
  add_dependencies(app ${target})
endfunction()
