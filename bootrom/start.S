/*
 * Copyright (c) 2026 Muhammad Waleed Badar
 * SPDX-License-Identifier: Apache-2.0
 *
 * Boot ROM stand-in for the RP2350 Renode model: vectors, the well-known
 * pointers that software looks up in the first words of the ROM, and the
 * part of the boot path that must not use a stack.
 *
 * Both cores leave reset here. Core 0 goes on to find and enter an image;
 * core 1 parks in the launch protocol until core 0 sends it a vector table,
 * a stack pointer and an entry point over the inter-core FIFO.
 */

#include "bootrom.h"

	.syntax unified
	.cpu cortex-m33
	.thumb

/* ------------------------------------------------------------------------ */
/* Vector table and well-known locations                                     */
/* ------------------------------------------------------------------------ */

	.section .vectors, "ax", %progbits
	.global __vectors
__vectors:
	.word	BOOTRAM_BASE + BOOTRAM_SIZE	/* 0x00: core 0 boot stack    */
	.word	rom_reset			/* 0x04: reset                */
	.word	rom_nmi				/* 0x08: NMI                  */
	.word	rom_hardfault			/* 0x0c: HardFault            */
	.byte	'M', 'u', 0x02			/* 0x10: RP2350 ROM magic     */
	.byte	ROM_VERSION			/* 0x13: ROM version          */
	.hword	rom_table			/* 0x14: function/data table  */
	.hword	rom_table_lookup + 1		/* 0x16: table lookup (Thumb) */
	.hword	rom_table_lookup_entry + 1	/* 0x18: entry lookup (Thumb) */
	.hword	0

/* ------------------------------------------------------------------------ */
/* Reset                                                                     */
/* ------------------------------------------------------------------------ */

	.section .text.rom_reset, "ax", %progbits
	.global rom_reset
	.thumb_func
rom_reset:
	ldr	r0, =SIO_BASE
	ldr	r1, [r0, #SIO_CPUID]
	cmp	r1, #0
	bne	rom_wait_for_vector

	/* Core 0: the C code runs on the boot stack in BOOTRAM. */
	ldr	r0, =BOOTRAM_BASE
	msr	msplim, r0
	bl	rom_boot_core0
	/* Nothing bootable: stay here so the PC names the reason. */
	.global rom_no_bootable_image
	.thumb_func
rom_no_bootable_image:
	wfi
	b	rom_no_bootable_image

	.global rom_nmi
	.thumb_func
rom_nmi:
	b	rom_nmi

	.global rom_hardfault
	.thumb_func
rom_hardfault:
	b	rom_hardfault

/* ------------------------------------------------------------------------ */
/* Entering an image                                                         */
/* ------------------------------------------------------------------------ */

/*
 * void rom_enter_image(uint32_t vtor, uint32_t sp, uint32_t pc,
 *                      uint32_t sp_limit);
 */
	.section .text.rom_enter_image, "ax", %progbits
	.global rom_enter_image
	.thumb_func
rom_enter_image:
	ldr	r4, =SCB_BASE
	str	r0, [r4, #SCB_VTOR]
	movs	r4, #0
	msr	msplim, r4
	msr	msp, r1
	msr	msplim, r3
	blx	r2
	.global rom_image_returned
	.thumb_func
rom_image_returned:
	wfi
	b	rom_image_returned

/* ------------------------------------------------------------------------ */
/* Core 1 launch protocol                                                    */
/* ------------------------------------------------------------------------ */

/*
 * Core 1 drains its FIFO and sends 0, then expects 1, the vector table, the
 * stack pointer and the entry point, echoing each word back. A 0 at any
 * point, or anything but 1 as the first word, restarts the sequence, which
 * is what makes the leading zeroes sent by core 0 line both sides up.
 *
 * No stack is used: core 1 has none until core 0 provides one.
 *
 * Also reachable as ROM function 'WV', for a core that has to give itself
 * back to the ROM.
 */
	.section .text.rom_wait_for_vector, "ax", %progbits
	.global rom_wait_for_vector
	.thumb_func
rom_wait_for_vector:
	ldr	r0, =SIO_BASE
	ldr	r4, =SCB_BASE
	movs	r1, #SCB_SCR_SLEEPDEEP
	str	r1, [r4, #SCB_SCR]

core1_restart:
	ldr	r1, [r0, #SIO_FIFO_ST]
	lsls	r1, r1, #(31 - SIO_FIFO_ST_VLD_BIT)
	bpl	1f
	ldr	r1, [r0, #SIO_FIFO_RD]
	b	core1_restart
1:
	movs	r1, #0
	bl	core1_send
	bl	core1_receive
	cmp	r1, #1
	bne	core1_restart
	bl	core1_send

	bl	core1_receive			/* vector table */
	mov	r5, r1
	bl	core1_send

	bl	core1_receive			/* stack pointer */
	mov	r6, r1
	bl	core1_send

	bl	core1_receive			/* entry point */
	mov	r7, r1
	bl	core1_send

	movs	r1, #0
	str	r1, [r4, #SCB_SCR]
	str	r5, [r4, #SCB_VTOR]
	msr	msp, r6
	blx	r7
	.global rom_core1_returned
	.thumb_func
rom_core1_returned:
	wfi
	b	rom_core1_returned

/* Push r1; waits for room. Clobbers r2. */
	.thumb_func
core1_send:
	ldr	r2, [r0, #SIO_FIFO_ST]
	lsls	r2, r2, #(31 - SIO_FIFO_ST_RDY_BIT)
	bmi	1f
	wfe
	b	core1_send
1:
	str	r1, [r0, #SIO_FIFO_WR]
	sev
	bx	lr

/* Pop into r1; waits for a word. A zero restarts the protocol. Clobbers r2. */
	.thumb_func
core1_receive:
	ldr	r2, [r0, #SIO_FIFO_ST]
	lsls	r2, r2, #(31 - SIO_FIFO_ST_VLD_BIT)
	bmi	1f
	wfe
	b	core1_receive
1:
	ldr	r1, [r0, #SIO_FIFO_RD]
	cmp	r1, #0
	beq	core1_restart
	bx	lr

	.ltorg

/* ------------------------------------------------------------------------ */
/* ROM table                                                                 */
/* ------------------------------------------------------------------------ */

/* See rom_table_lookup_entry() for the layout. */
	.macro rom_func c1, c2, function
	.byte	\c1, \c2
	.hword	RT_FLAG_FUNC_ARM_SEC
	.hword	\function + 1
	.endm

	.section .rodata.rom_table, "a", %progbits
	.balign	2
	.global rom_table
rom_table:
	rom_func 'R', 'B', rom_reboot
	rom_func 'S', 'R', rom_bootrom_state_reset
	rom_func 'S', 'S', rom_set_bootrom_stack
	rom_func 'W', 'V', rom_wait_for_vector
	.hword	0
