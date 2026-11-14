/*
 * Copyright (c) 2026 Muhammad Waleed Badar
 * SPDX-License-Identifier: Apache-2.0
 *
 * The RP2350 registers the dual-core sample touches, and nothing else.
 */

#ifndef RP2350_H
#define RP2350_H

#ifndef __ASSEMBLER__
#include <stdint.h>

#define REG32(address)		(*(volatile uint32_t *)(address))
#endif

/* Every block below SIO has aliases that set and clear bits atomically. */
#define REG_SET(address)	REG32((address) + 0x2000)
#define REG_CLR(address)	REG32((address) + 0x3000)

/* Clocks */

#define CLOCKS_BASE		0x40010000
#define CLK_REF_CTRL		(CLOCKS_BASE + 0x30)
#define CLK_REF_SELECTED	(CLOCKS_BASE + 0x38)
#define CLK_SYS_CTRL		(CLOCKS_BASE + 0x3c)
#define CLK_SYS_DIV		(CLOCKS_BASE + 0x40)
#define CLK_SYS_SELECTED	(CLOCKS_BASE + 0x44)
#define CLK_PERI_CTRL		(CLOCKS_BASE + 0x48)

#define CLK_REF_CTRL_SRC_XOSC		0x2
#define CLK_SYS_CTRL_SRC_AUX		0x1
#define CLK_SYS_CTRL_AUXSRC_PLL_SYS	(0x0 << 5)
#define CLK_PERI_CTRL_AUXSRC_CLK_SYS	(0x0 << 5)
#define CLK_PERI_CTRL_ENABLE		(1u << 11)

#define XOSC_BASE		0x40048000
#define XOSC_CTRL		(XOSC_BASE + 0x00)
#define XOSC_STATUS		(XOSC_BASE + 0x04)
#define XOSC_STARTUP		(XOSC_BASE + 0x0c)

#define XOSC_CTRL_RANGE_1_15MHZ	0xaa0
#define XOSC_CTRL_ENABLE	(0xfabu << 12)
#define XOSC_STATUS_STABLE	(1u << 31)

#define PLL_SYS_BASE		0x40050000
#define PLL_CS			0x00
#define PLL_PWR			0x04
#define PLL_FBDIV_INT		0x08
#define PLL_PRIM		0x0c

#define PLL_CS_LOCK		(1u << 31)
#define PLL_PWR_PD		(1u << 0)
#define PLL_PWR_POSTDIVPD	(1u << 3)
#define PLL_PWR_VCOPD		(1u << 5)

#define TICKS_BASE		0x40108000
#define TICKS_TIMER0_CTRL	(TICKS_BASE + 0x18)
#define TICKS_TIMER0_CYCLES	(TICKS_BASE + 0x1c)
#define TICKS_CTRL_ENABLE	(1u << 0)

/* Resets and power */

#define RESETS_BASE		0x40020000
#define RESETS_RESET		(RESETS_BASE + 0x0)
#define RESETS_RESET_DONE	(RESETS_BASE + 0x8)

#define RESET_IO_BANK0		(1u << 6)
#define RESET_PADS_BANK0	(1u << 9)
#define RESET_PLL_SYS		(1u << 14)
#define RESET_TIMER0		(1u << 23)
#define RESET_UART0		(1u << 26)

#define PSM_BASE		0x40018000
#define PSM_FRCE_OFF		(PSM_BASE + 0x4)
#define PSM_FRCE_OFF_PROC1	(1u << 24)

/* Pins */

#define IO_BANK0_BASE		0x40028000
#define IO_BANK0_GPIO_CTRL(n)	(IO_BANK0_BASE + 0x004 + 8 * (n))
#define GPIO_FUNC_UART		2

#define PADS_BANK0_BASE		0x40038000
#define PADS_BANK0_GPIO(n)	(PADS_BANK0_BASE + 0x04 + 4 * (n))
#define PADS_IE			(1u << 6)
#define PADS_ISO		(1u << 8)

/* UART (PL011) */

#define UART0_BASE		0x40070000
#define UART_DR			(UART0_BASE + 0x00)
#define UART_FR			(UART0_BASE + 0x18)
#define UART_IBRD		(UART0_BASE + 0x24)
#define UART_FBRD		(UART0_BASE + 0x28)
#define UART_LCR_H		(UART0_BASE + 0x2c)
#define UART_CR			(UART0_BASE + 0x30)

#define UART_FR_TXFF		(1u << 5)
#define UART_LCR_H_FEN		(1u << 4)
#define UART_LCR_H_WLEN_8	(3u << 5)
#define UART_CR_UARTEN		(1u << 0)
#define UART_CR_TXE		(1u << 8)
#define UART_CR_RXE		(1u << 9)

/* Timer */

#define TIMER0_BASE		0x400b0000
#define TIMER_ALARM(n)		(TIMER0_BASE + 0x10 + 4 * (n))
#define TIMER_TIMERAWL		(TIMER0_BASE + 0x28)
#define TIMER_INTR		(TIMER0_BASE + 0x3c)
#define TIMER_INTE		(TIMER0_BASE + 0x40)

/* SIO */

#define SIO_BASE		0xd0000000
#define SIO_CPUID		(SIO_BASE + 0x000)
#define SIO_FIFO_ST		(SIO_BASE + 0x050)
#define SIO_FIFO_WR		(SIO_BASE + 0x054)
#define SIO_FIFO_RD		(SIO_BASE + 0x058)
#define SIO_SPINLOCK_ST		(SIO_BASE + 0x05c)
#define SIO_SPINLOCK(n)		(SIO_BASE + 0x100 + 4 * (n))
#define SIO_DOORBELL_OUT_SET	(SIO_BASE + 0x180)
#define SIO_DOORBELL_IN_CLR	(SIO_BASE + 0x18c)

#define SIO_FIFO_ST_VLD		(1u << 0)
#define SIO_FIFO_ST_RDY		(1u << 1)
#define SIO_FIFO_ST_WOF		(1u << 2)
#define SIO_FIFO_ST_ROE		(1u << 3)
#define SIO_FIFO_DEPTH		4

/* Cortex-M33 */

#define SYST_CSR		0xe000e010
#define SYST_RVR		0xe000e014
#define SYST_CVR		0xe000e018
#define SYST_CSR_ENABLE		(1u << 0)
#define SYST_CSR_TICKINT	(1u << 1)
#define SYST_CSR_CLKSOURCE	(1u << 2)

#define NVIC_ISER(n)		(0xe000e100 + 4 * (n))
#define NVIC_ICER(n)		(0xe000e180 + 4 * (n))
#define NVIC_ICPR(n)		(0xe000e280 + 4 * (n))

/* Interrupt numbers */

#define IRQ_TIMER0_ALARM(n)	(n)
#define IRQ_SIO_FIFO		25
#define IRQ_SIO_BELL		26
#define NUM_IRQS		52

#endif /* RP2350_H */
