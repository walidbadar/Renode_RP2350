/*
 * Copyright (c) 2026 Muhammad Waleed Badar
 * SPDX-License-Identifier: GPL-3.0-only
 *
 * Addresses and constants shared by the assembly and C halves of the ROM.
 */

#ifndef BOOTROM_H
#define BOOTROM_H

/* Reported at 0x13. Matches the A2 silicon ROM so that software which keys
 * the layout of the well-known pointers on the version keeps working. */
#define ROM_VERSION		2

#define XIP_BASE		0x10000000
#define SRAM_BASE		0x20000000

/* 1 kB of RAM that only the ROM uses, so booting never disturbs main SRAM. */
#define BOOTRAM_BASE		0x400e0000
#define BOOTRAM_SIZE		0x400

#define SIO_BASE		0xd0000000
#define SIO_CPUID		0x000
#define SIO_FIFO_ST		0x050
#define SIO_FIFO_WR		0x054
#define SIO_FIFO_RD		0x058
#define SIO_FIFO_ST_VLD_BIT	0
#define SIO_FIFO_ST_RDY_BIT	1

/* ROM table flag: the entry is a function for Arm, Secure state. */
#define RT_FLAG_FUNC_ARM_SEC	0x0004

#define SCB_BASE		0xe000ed00
#define SCB_VTOR		0x08
#define SCB_AIRCR		0x0c
#define SCB_SCR			0x10
#define SCB_SCR_SLEEPDEEP	0x4
#define SCB_AIRCR_SYSRESETREQ	0x05fa0004

#endif /* BOOTROM_H */
