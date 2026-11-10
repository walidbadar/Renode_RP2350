# Copyright (c) 2026 Muhammad Waleed Badar
# SPDX-License-Identifier: GPL-3.0-only
#
# Builds what the model needs besides Renode itself, the boot ROM stand-in,
# and the samples; runs the tests.
#
#   make            boot ROM and bare-metal sample
#   make run        the sample on the Pico 2, in Renode
#   make zephyr     Zephyr samples (needs ZEPHYR_BASE)
#   make pico-sdk   Pico SDK sample (needs PICO_SDK_PATH or ZEPHYR_BASE)
#   make test       every test in tests/

# Any bare-metal Arm GCC will do. The one from the Zephyr SDK is used when it
# is installed in its usual place, arm-none-eabi-gcc from PATH otherwise.
ZEPHYR_SDK_INSTALL_DIR ?= $(lastword $(sort $(wildcard $(HOME)/zephyr-sdk-*)))
ifneq ($(wildcard $(ZEPHYR_SDK_INSTALL_DIR)/gnu/arm-zephyr-eabi/bin/arm-zephyr-eabi-gcc),)
CROSS_COMPILE ?= $(ZEPHYR_SDK_INSTALL_DIR)/gnu/arm-zephyr-eabi/bin/arm-zephyr-eabi-
else ifneq ($(wildcard $(ZEPHYR_SDK_INSTALL_DIR)/arm-zephyr-eabi/bin/arm-zephyr-eabi-gcc),)
CROSS_COMPILE ?= $(ZEPHYR_SDK_INSTALL_DIR)/arm-zephyr-eabi/bin/arm-zephyr-eabi-
else
CROSS_COMPILE ?= arm-none-eabi-
endif

CC      := $(CROSS_COMPILE)gcc
OBJDUMP := $(CROSS_COMPILE)objdump
SIZE    := $(CROSS_COMPILE)size

RENODE ?= renode
# renode-test and the list of Python packages it needs live next to renode.
RENODE_ROOT := $(patsubst %/,%,$(dir $(realpath $(shell command -v $(RENODE)))))
RENODE_TEST ?= $(RENODE_ROOT)/renode-test

BUILD ?= build

# No FPU code: neither program enables the coprocessor.
CFLAGS := -mcpu=cortex-m33 -mthumb -mfloat-abi=soft \
	  -Os -g -ffreestanding -fno-builtin -ffunction-sections -fdata-sections \
	  -Wall -Wextra -Werror
LDFLAGS := -nostdlib -Wl,--gc-sections -Wl,--no-warn-rwx-segments

.PHONY: all bootrom multicore zephyr pico-sdk run test clean

all: bootrom multicore

# -- Boot ROM ----------------------------------------------------------------

BOOTROM_SRCS := bootrom/start.S bootrom/bootrom.c
BOOTROM_ELF  := $(BUILD)/bootrom/bootrom.elf

bootrom: $(BOOTROM_ELF)

$(BOOTROM_ELF): $(BOOTROM_SRCS) bootrom/bootrom.h bootrom/bootrom.ld
	@mkdir -p $(dir $@)
	$(CC) $(CFLAGS) $(LDFLAGS) -T bootrom/bootrom.ld -Wl,-Map=$(@:.elf=.map) \
		-o $@ $(BOOTROM_SRCS)
	$(OBJDUMP) -d $@ > $(@:.elf=.lst)
	@$(SIZE) $@

# -- Dual-core sample --------------------------------------------------------

MULTICORE_SRCS := samples/multicore/startup.S samples/multicore/main.c
MULTICORE_ELF  := $(BUILD)/multicore/multicore.elf

multicore: $(MULTICORE_ELF)

$(MULTICORE_ELF): $(MULTICORE_SRCS) samples/multicore/rp2350.h samples/multicore/multicore.ld
	@mkdir -p $(dir $@)
	$(CC) $(CFLAGS) $(LDFLAGS) -T samples/multicore/multicore.ld -Wl,-Map=$(@:.elf=.map) \
		-o $@ $(MULTICORE_SRCS)
	$(OBJDUMP) -d $@ > $(@:.elf=.lst)
	@$(SIZE) $@

# -- Zephyr samples ----------------------------------------------------------

# Stock samples from the Zephyr tree, built unchanged for the Pico 2, and the
# sample in samples/zephyr_core1. The tests in tests/zephyr.robot run them
# and are skipped while they are not built. ZEPHYR_BASE has to point at the
# zephyr directory of a workspace.
ZEPHYR_BOARD   ?= rpi_pico2/rp2350a/m33
ZEPHYR_SAMPLES := hello_world synchronization basic/blinky \
		  subsys/shell/shell_module drivers/counter/alarm drivers/watchdog

zephyr:
	@test -n "$(ZEPHYR_BASE)" || { echo "Set ZEPHYR_BASE to the zephyr directory of a workspace"; exit 1; }
	@for sample in $(addprefix $(ZEPHYR_BASE)/samples/,$(ZEPHYR_SAMPLES)) $(CURDIR)/samples/zephyr_core1; do \
		echo "== $$sample"; \
		(cd $(ZEPHYR_BASE) && west build -b $(ZEPHYR_BOARD) \
			-d $(abspath $(BUILD))/zephyr/$$(basename $$sample) $$sample) || exit 1; \
	done

# -- Pico SDK sample ---------------------------------------------------------

# The dual-core checks again, through the Raspberry Pi Pico SDK. Any copy of
# the SDK will do; a Zephyr workspace has one in modules/hal/rpi_pico. Only
# the ELF file is wanted, so picotool is not needed.
PICO_SDK_PATH ?= $(if $(ZEPHYR_BASE),$(abspath $(ZEPHYR_BASE)/../modules/hal/rpi_pico))
PICO_TOOLCHAIN := -DPICO_GCC_TRIPLE=$(patsubst %-,%,$(notdir $(CROSS_COMPILE))) \
		  $(if $(findstring /,$(CROSS_COMPILE)),-DPICO_TOOLCHAIN_PATH=$(abspath $(dir $(CROSS_COMPILE))..))

pico-sdk:
	@test -n "$(PICO_SDK_PATH)" || { echo "Set PICO_SDK_PATH, or ZEPHYR_BASE to use the SDK in a Zephyr workspace"; exit 1; }
	PICO_SDK_PATH=$(PICO_SDK_PATH) cmake -S samples/pico_sdk -B $(BUILD)/pico_sdk \
		-DPICO_BOARD=pico2 -DPICO_PLATFORM=rp2350-arm-s -DPICO_NO_PICOTOOL=1 $(PICO_TOOLCHAIN)
	cmake --build $(BUILD)/pico_sdk

# -- Running -----------------------------------------------------------------

run: all
	$(RENODE) scripts/rpi_pico2.resc

# renode-test is a Robot Framework runner; its Python packages go into a
# virtual environment of their own.
VENV := .venv

$(VENV)/bin/robot:
	python3 -m venv $(VENV)
	$(VENV)/bin/pip install -r $(RENODE_ROOT)/tests/requirements.txt

# TESTS narrows the run to some of the test files; TEST_ARGS goes to
# renode-test as it is, for example "-n 5" to repeat every test five times.
TESTS ?= $(sort $(wildcard tests/*.robot))

test: all $(VENV)/bin/robot
	PATH="$(abspath $(VENV)/bin):$$PATH" $(RENODE_TEST) -r $(BUILD)/tests $(TEST_ARGS) $(TESTS)

clean:
	rm -rf $(BUILD)
