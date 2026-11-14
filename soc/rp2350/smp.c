/*
 * Copyright (c) 2026 Muhammad Waleed Badar
 * SPDX-License-Identifier: Apache-2.0
 */

#include <zephyr/kernel.h>
#include <zephyr/drivers/pm_cpu_ops.h>
#include <zephyr/irq.h>
#include <zephyr/platform/hooks.h>
#include <zephyr/sys/util.h>

#include <cmsis_core.h>
#include <ipi.h>
#include <hardware/regs/intctrl.h>
#include <hardware/structs/sio.h>
#include <pico/runtime_init.h>

BUILD_ASSERT(CONFIG_MP_MAX_NUM_CPUS <= 2, "the RP2350 has two cores");

#define CORE1_HWID DT_REG_ADDR(DT_NODELABEL(cpu1))

/* How long core 1 gets to answer one word of the launch sequence. */
#define LAUNCH_TIMEOUT K_MSEC(100)

/* The doorbell, out of the eight between the cores, that the kernel rings. */
#define IPI_DOORBELL BIT(0)

K_KERNEL_STACK_ARRAY_DECLARE(z_interrupt_stacks, CONFIG_MP_MAX_NUM_CPUS, CONFIG_ISR_STACK_SIZE);

/* Called from the reset code before there is a stack, so it uses r0 only. */
__attribute__((naked)) uint32_t soc_cpu_index(void)
{
	__asm__ volatile("ldr r0, =%c0\n\t"
			 "ldr r0, [r0]\n\t"
			 "bx lr\n\t"
			 ".ltorg\n\t"
			 :
			 : "i"(SIO_BASE + SIO_CPUID_OFFSET));
}

static bool fifo_pop(uint32_t *value)
{
	k_timepoint_t end = sys_timepoint_calc(LAUNCH_TIMEOUT);

	while ((sio_hw->fifo_st & SIO_FIFO_ST_VLD_BITS) == 0U) {
		if (sys_timepoint_expired(end)) {
			return false;
		}
	}
	*value = sio_hw->fifo_rd;

	return true;
}

/*
 * Core 1 waits in the boot ROM until it has been sent, and has echoed, six
 * words through the FIFO between the cores: 0, 0, 1, then the vector table,
 * the stack pointer and the entry point. A wrong echo restarts the sequence.
 *
 * The entry point is the reset vector, as on core 0. The reset code moves to
 * the stack that arch_cpu_start() chose, the interrupt stack of core 1, so
 * core 1 is launched on that stack too.
 */
int pm_cpu_on(unsigned long cpuid, uintptr_t entry_point)
{
	const uint32_t *vectors = (const uint32_t *)entry_point;
	const uint32_t sequence[] = {
		0,
		0,
		1,
		entry_point,
		(uint32_t)K_KERNEL_STACK_BUFFER(z_interrupt_stacks[1]) +
			K_KERNEL_STACK_SIZEOF(z_interrupt_stacks[1]),
		vectors[1],
	};
	bool fifo_irq = irq_is_enabled(SIO_IRQ_FIFO);
	unsigned int attempts = 0;
	size_t i = 0;
	int ret = 0;

	if (cpuid != CORE1_HWID) {
		return -EINVAL;
	}

	/* The answers are for this function, not for a mailbox driver. */
	irq_disable(SIO_IRQ_FIFO);

	while (i < ARRAY_SIZE(sequence)) {
		uint32_t word = sequence[i];
		uint32_t echo;

		if (word == 0U) {
			while ((sio_hw->fifo_st & SIO_FIFO_ST_VLD_BITS) != 0U) {
				(void)sio_hw->fifo_rd;
			}
			__SEV();
		}

		while ((sio_hw->fifo_st & SIO_FIFO_ST_RDY_BITS) == 0U) {
		}
		sio_hw->fifo_wr = word;
		__SEV();

		if (!fifo_pop(&echo) || (echo != word)) {
			if (++attempts == 10U) {
				ret = -ETIMEDOUT;
				break;
			}
			i = 0;
			continue;
		}
		i++;
	}

	if (fifo_irq) {
		irq_enable(SIO_IRQ_FIFO);
	}

	return ret;
}

/* Runs on each core: the NVIC and the coprocessor enables are per core. */
void soc_per_core_init_hook(void)
{
	if (soc_cpu_index() != 0U) {
		runtime_init_per_core_enable_coprocessors();
	}

#ifdef CONFIG_SMP
	sio_hw->doorbell_in_clr = IPI_DOORBELL;
	arm_irq_priority_set(SIO_IRQ_BELL, 0, 0);
	irq_enable(SIO_IRQ_BELL);
#endif
}

#ifdef CONFIG_SMP
static void ipi_isr(const void *arg)
{
	ARG_UNUSED(arg);

	sio_hw->doorbell_in_clr = IPI_DOORBELL;
	z_sched_ipi();
}

/* DOORBELL_OUT_SET rings on the other core, whichever one this is. */
void arch_sched_directed_ipi(uint32_t cpu_bitmap)
{
	if ((cpu_bitmap & BIT(soc_cpu_index() ^ 1U)) != 0U) {
		sio_hw->doorbell_out_set = IPI_DOORBELL;
	}
}

void arch_sched_broadcast_ipi(void)
{
	sio_hw->doorbell_out_set = IPI_DOORBELL;
}

int arch_smp_init(void)
{
	/* One handler for both cores; each enables its own doorbell line. */
	IRQ_CONNECT(SIO_IRQ_BELL, 0, ipi_isr, NULL, 0);

	return 0;
}
#endif /* CONFIG_SMP */
