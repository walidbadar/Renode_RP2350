/*
 * Copyright (c) 2026 Muhammad Waleed Badar
 * SPDX-License-Identifier: Apache-2.0
 *
 * The part of the sample that concerns core 1: launching it, and what it
 * runs.
 *
 * Zephyr knows nothing about core 1 here. The worker is a plain function
 * with a stack and a vector table of its own; it calls nothing in the
 * kernel, takes no interrupts, and talks to core 0 through the mailbox only.
 */

#include <zephyr/kernel.h>
#include <zephyr/toolchain.h>

#include <cmsis_core.h>
#include <hardware/structs/sio.h>

#include "core1.h"

#define CORE1_STACK_WORDS	256

static uint32_t core1_stack[CORE1_STACK_WORDS];

static void core1_fault(void)
{
	for (;;) {
	}
}

/* Answer every number that arrives with its square. */
static void core1_worker(void)
{
	sio_hw->fifo_wr = CORE1_HELLO | sio_hw->cpuid;
	__SEV();

	for (;;) {
		uint32_t n;

		while ((sio_hw->fifo_st & SIO_FIFO_ST_VLD_BITS) == 0) {
			__WFE();
		}
		n = sio_hw->fifo_rd;

		while ((sio_hw->fifo_st & SIO_FIFO_ST_RDY_BITS) == 0) {
		}
		sio_hw->fifo_wr = n * n;
		__SEV();
	}
}

/*
 * Zephyr's vector table leads into its interrupt handling, which keeps its
 * state per CPU and has only one here. Core 1 gets a table that keeps it
 * out of there: nothing but faults can happen, and they stop the core.
 */
static const uint32_t core1_vectors[16] __aligned(256) = {
	(uint32_t)&core1_stack[CORE1_STACK_WORDS],
	(uint32_t)core1_worker,
	[2 ... 15] = (uint32_t)core1_fault,
};

/* Returns false if nothing arrives within the timeout. */
static bool fifo_pop(uint32_t *value, k_timeout_t timeout)
{
	k_timepoint_t end = sys_timepoint_calc(timeout);

	while ((sio_hw->fifo_st & SIO_FIFO_ST_VLD_BITS) == 0) {
		if (sys_timepoint_expired(end)) {
			return false;
		}
	}
	*value = sio_hw->fifo_rd;
	return true;
}

/*
 * The boot ROM holds core 1 until it has been sent, and has echoed, this
 * sequence. Any wrong echo means the two sides are out of step, and the
 * zeroes at the start are there to get them back in step.
 */
bool core1_launch(void)
{
	const uint32_t sequence[] = {
		0, 0, 1,
		(uint32_t)core1_vectors,
		(uint32_t)&core1_stack[CORE1_STACK_WORDS],
		(uint32_t)core1_worker,
	};
	k_timepoint_t end = sys_timepoint_calc(K_MSEC(100));
	unsigned int i = 0;

	while (i < ARRAY_SIZE(sequence)) {
		uint32_t word = sequence[i];
		uint32_t echo;

		if (sys_timepoint_expired(end)) {
			return false;
		}
		if (word == 0) {
			while ((sio_hw->fifo_st & SIO_FIFO_ST_VLD_BITS) != 0) {
				(void)sio_hw->fifo_rd;
			}
			__SEV();
		}
		sio_hw->fifo_wr = word;
		__SEV();
		if (!fifo_pop(&echo, K_MSEC(10))) {
			return false;
		}
		i = (echo == word) ? i + 1 : 0;
	}

	return true;
}
