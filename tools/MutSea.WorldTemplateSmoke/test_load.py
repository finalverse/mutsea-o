# Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
import unittest
from load import overlap, utc_ticks


class SessionIntervals(unittest.TestCase):
    def test_half_open_intervals_do_not_invent_concurrency(self):
        self.assertEqual(overlap([(0,1),(1,2)]),1)
        self.assertEqual(overlap([(0,10),(2,12),(3,9)]),3)
        self.assertEqual(overlap([(0,4),(2,6),(5,9)]),2)

    def test_invalid_interval_is_refused(self):
        for interval in [(1,1),(2,1),(0,float('inf')),(float('nan'),1)]:
            with self.assertRaises(ValueError):overlap([interval])

    def test_native_fractional_timestamps_preserve_exact_interval_order(self):
        self.assertEqual(utc_ticks('2026-10-14T11:45:34.11701+00:00'),utc_ticks('2026-10-14T11:45:34.1170100+00:00'))
        self.assertEqual(utc_ticks('2026-10-14T11:45:34.1170101+00:00')-utc_ticks('2026-10-14T11:45:34.1170100+00:00'),1)
        with self.assertRaises(ValueError):utc_ticks('2026-10-14T11:45:34+01:00')


if __name__=='__main__':unittest.main()
