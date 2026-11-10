/*
 * Copyright (c) 2026 Muhammad Waleed Badar
 * SPDX-License-Identifier: GPL-3.0-only
 *
 * The ROM is 32 kB at address 0 and has no writable data: its only RAM is
 * the stack that start.S points at BOOTRAM.
 */

MEMORY
{
	ROM (rx) : ORIGIN = 0x00000000, LENGTH = 32K
}

ENTRY(rom_reset)

SECTIONS
{
	.text :
	{
		KEEP(*(.vectors))
		*(.text .text.*)
		*(.rodata .rodata.*)
		. = ALIGN(4);
	} > ROM

	.writable (NOLOAD) :
	{
		*(.data .data.* .bss .bss.* COMMON)
	} > ROM

	ASSERT(SIZEOF(.writable) == 0, "the ROM cannot hold writable data")

	/DISCARD/ :
	{
		*(.ARM.exidx* .ARM.extab* .comment .note.*)
	}
}
