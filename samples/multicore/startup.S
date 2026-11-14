/*
 * Copyright (c) 2026 Muhammad Waleed Badar
 * SPDX-License-Identifier: Apache-2.0
 *
 * Vector table, image definition and reset code for the dual-core sample.
 */

#include "rp2350.h"

	.syntax unified
	.cpu cortex-m33
	.thumb

/* One table for both cores: the handlers ask the SIO which core they run on. */
	.section .vectors, "a", %progbits
	.global __vectors
__vectors:
	.word	__stack_top
	.word	reset_handler
	.word	fault_handler		/* NMI */
	.word	fault_handler		/* HardFault */
	.word	fault_handler		/* MemManage */
	.word	fault_handler		/* BusFault */
	.word	fault_handler		/* UsageFault */
	.word	fault_handler		/* SecureFault */
	.word	0
	.word	0
	.word	0
	.word	fault_handler		/* SVCall */
	.word	fault_handler		/* DebugMonitor */
	.word	0
	.word	fault_handler		/* PendSV */
	.word	systick_handler

	.word	unexpected_irq		/*  0: TIMER0 alarm 0 */
	.word	timer_alarm1_handler	/*  1: TIMER0 alarm 1 */
	.rept	IRQ_SIO_FIFO - 2
	.word	unexpected_irq
	.endr
	.word	sio_fifo_handler	/* 25: this core's mailbox */
	.word	sio_bell_handler	/* 26: this core's doorbells */
	.rept	NUM_IRQS - IRQ_SIO_BELL - 1
	.word	unexpected_irq
	.endr

/*
 * The boot ROM only starts an image that has an IMAGE_DEF block within its
 * first 4 kB: here an Arm executable for the RP2350, entered through the
 * vector table at the start of the image.
 */
	.section .image_def, "a", %progbits
	.word	0xffffded3		/* block start */
	.word	0x10210142		/* IMAGE_DEF: executable, Secure, Arm, RP2350 */
	.word	0x000001ff		/* last item; one word of items before it */
	.word	0x00000000		/* no further block */
	.word	0xab123579		/* block end */

	.section .text.reset_handler, "ax", %progbits
	.global reset_handler
	.thumb_func
reset_handler:
	ldr	r0, =__data_load
	ldr	r1, =__data_start
	ldr	r2, =__data_end
1:
	cmp	r1, r2
	bhs	2f
	ldr	r3, [r0], #4
	str	r3, [r1], #4
	b	1b
2:
	ldr	r1, =__bss_start
	ldr	r2, =__bss_end
	movs	r3, #0
3:
	cmp	r1, r2
	bhs	4f
	str	r3, [r1], #4
	b	3b
4:
	bl	main
5:
	wfi
	b	5b

	.ltorg
