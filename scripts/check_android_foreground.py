"""Read resumed-activity records, not historical mentions of the package."""
import re
import sys
from pathlib import Path


def is_foreground(dump: str, package: str) -> bool:
    # Prefer the authoritative top-resumed record when Android supplies it.
    for label in ('topResumedActivity', 'mResumedActivity', 'ResumedActivity', 'Resumed'):
        records = re.findall(r'^\s*' + label + r'\s*[:=]\s*ActivityRecord\{([^\r\n]+)', dump, re.MULTILINE)
        if records:
            return any(re.search(r'\s' + re.escape(package) + r'/', record) for record in records)
    return False


if __name__ == '__main__':
    if not is_foreground(Path(sys.argv[1]).read_text(), sys.argv[2]):
        sys.exit('FAIL: AniKatha is not the resumed foreground activity')
    print('PASS: AniKatha is the resumed foreground activity.')
