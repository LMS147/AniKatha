import unittest
from check_android_foreground import is_foreground

PACKAGE = 'co.zyma.anikatha'

class ForegroundChecks(unittest.TestCase):
    def test_android_15_record(self):
        self.assertTrue(is_foreground('    topResumedActivity=ActivityRecord{540cdc3 u0 co.zyma.anikatha/crc.MainActivity t8}', PACKAGE))

    def test_older_records(self):
        for label in ('mResumedActivity:', 'ResumedActivity:', 'Resumed:'):
            with self.subTest(label=label):
                self.assertTrue(is_foreground(f'  {label} ActivityRecord{{abc u0 {PACKAGE}/.MainActivity t8}}', PACKAGE))

    def test_background_package_is_not_enough(self):
        dump = 'Hist #0: ActivityRecord{abc u0 co.zyma.anikatha/.MainActivity t8}\n topResumedActivity=ActivityRecord{def u0 com.android.launcher/.Home t9}'
        self.assertFalse(is_foreground(dump, PACKAGE))

    def test_top_record_wins_over_stale_lower_priority_record(self):
        dump = 'topResumedActivity=ActivityRecord{abc u0 com.android.launcher/.Home t9}\nmResumedActivity: ActivityRecord{def u0 co.zyma.anikatha/.MainActivity t8}'
        self.assertFalse(is_foreground(dump, PACKAGE))

    def test_package_prefix_and_absent_records_are_rejected(self):
        self.assertFalse(is_foreground('topResumedActivity=ActivityRecord{abc u0 co.zyma.anikatha.other/.MainActivity t8}', PACKAGE))
        self.assertFalse(is_foreground('packageName=co.zyma.anikatha', PACKAGE))

if __name__ == '__main__':
    unittest.main()
