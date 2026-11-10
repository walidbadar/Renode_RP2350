/*
 * Copyright (c) 2026 Muhammad Waleed Badar
 * SPDX-License-Identifier: GPL-3.0-only
 *
 * Zephyr on core 0 puts core 1 to work.
 *
 * Zephyr on the RP2350 runs on one core and leaves the other parked in the
 * boot ROM. This sample launches a small worker there and hands it numbers
 * to square, using Zephyr's own driver for the inter-core mailbox.
 */

#include <zephyr/device.h>
#include <zephyr/drivers/mbox.h>
#include <zephyr/kernel.h>

#include <hardware/structs/sio.h>

#include "core1.h"

#define REQUESTS	5

static K_SEM_DEFINE(answered, 0, 1);
static uint32_t answer;

/* Called from the mailbox interrupt on core 0. */
static void received(const struct device *dev, mbox_channel_id_t channel, void *user_data,
		     struct mbox_msg *msg)
{
	ARG_UNUSED(dev);
	ARG_UNUSED(channel);
	ARG_UNUSED(user_data);

	answer = *(const uint32_t *)msg->data;
	k_sem_give(&answered);
}

int main(void)
{
	const struct device *mbox = DEVICE_DT_GET(DT_NODELABEL(mbox));
	unsigned int answers = 0;
	uint32_t hello;

	printk("Zephyr on core %u\n", sio_hw->cpuid);

	if (!core1_launch()) {
		printk("core 1 did not answer the launch sequence\n");
		return 0;
	}

	/* The worker's first word says which core it found itself on. */
	while ((sio_hw->fifo_st & SIO_FIFO_ST_VLD_BITS) == 0) {
	}
	hello = sio_hw->fifo_rd;
	printk("worker on core %u\n", hello & ~CORE1_HELLO);

	mbox_register_callback(mbox, 0, received, NULL);
	mbox_set_enabled(mbox, 0, true);

	for (uint32_t n = 1; n <= REQUESTS; n++) {
		struct mbox_msg msg = { .data = &n, .size = sizeof(n) };

		if (mbox_send(mbox, 0, &msg) != 0 || k_sem_take(&answered, K_MSEC(100)) != 0) {
			printk("no answer for %u\n", n);
			continue;
		}
		printk("%u squared is %u\n", n, answer);
		answers += (answer == n * n);
	}

	printk("core 1 answered %u of %u requests\n", answers, REQUESTS);

	return 0;
}
