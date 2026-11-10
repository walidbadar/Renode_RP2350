/*
 * Copyright (c) 2026 Muhammad Waleed Badar
 * SPDX-License-Identifier: Apache-2.0
 *
 * The sample runs in place from flash, with its data in main SRAM.
 */

MEMORY
{
	FLASH (rx)  : ORIGIN = 0x10000000, LENGTH = 4M
	RAM   (rwx) : ORIGIN = 0x20000000, LENGTH = 512K
}

ENTRY(reset_handler)

SECTIONS
{
	.text :
	{
		KEEP(*(.vectors))
		KEEP(*(.image_def))
		*(.text .text.*)
		*(.rodata .rodata.*)
		. = ALIGN(4);
	} > FLASH

	ASSERT(SIZEOF(.text) > 0, "empty image")

	.data :
	{
		__data_start = .;
		*(.data .data.*)
		. = ALIGN(4);
		__data_end = .;
	} > RAM AT > FLASH
	__data_load = LOADADDR(.data);

	.bss (NOLOAD) :
	{
		__bss_start = .;
		*(.bss .bss.* COMMON)
		. = ALIGN(4);
		__bss_end = .;
	} > RAM

	/* Core 0 runs on the stack at the top of RAM, core 1 on its own array. */
	__stack_top = ORIGIN(RAM) + LENGTH(RAM);

	/DISCARD/ :
	{
		*(.ARM.exidx* .ARM.extab* .comment .note.*)
	}
}
