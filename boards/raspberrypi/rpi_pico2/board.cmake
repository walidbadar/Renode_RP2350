# Copyright (c) 2026 Muhammad Waleed Badar
# SPDX-License-Identifier: Apache-2.0

# Lets "west build -t run" start an image for the Pico 2 in Renode.
set(SUPPORTED_EMU_PLATFORMS renode)
set(RENODE_SCRIPT ${CMAKE_CURRENT_LIST_DIR}/../../../scripts/rpi_pico2.resc)
set(RENODE_UART sysbus.uart0)
