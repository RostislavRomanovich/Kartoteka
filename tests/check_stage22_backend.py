from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
checks = []
def check(label, condition):
    checks.append((label, bool(condition)))

entities = (ROOT / 'Models/Entities.cs').read_text(encoding='utf-8')
context = (ROOT / 'Data/AppDbContext.cs').read_text(encoding='utf-8')
alerts = (ROOT / 'Controllers/AlertsController.cs').read_text(encoding='utf-8')
medical = (ROOT / 'Controllers/EmployeeExtendedDataController.cs').read_text(encoding='utf-8')
psych = (ROOT / 'Controllers/EmployeeConfidentialHealthController.cs').read_text(encoding='utf-8')
leaves = (ROOT / 'Controllers/EmployeeLeavesController.cs').read_text(encoding='utf-8')
entitlement = (ROOT / 'Controllers/EmployeeLeaveEntitlementsController.cs').read_text(encoding='utf-8')
planning = (ROOT / 'Controllers/LeavePlanningController.cs').read_text(encoding='utf-8')
sql = (ROOT / 'scripts/03_stage22_employee_extensions_and_leaves.sql').read_text(encoding='utf-8')

check('extended employee fields model exists', 'class EmployeeExtendedData' in entities and all(x in entities for x in ['DactyloscopyDate', 'BloodGroup', 'DnaTestDate', 'UmoExpiresOn', 'HeightCm', 'WeightKg', 'ClothingSize', 'ShoeSize']))
check('psych data has separate entity', 'class EmployeeConfidentialHealthRecord' in entities)
check('psych endpoint is admin-only', '[Authorize(Roles = "admin")]' in psych)
check('extended personal data endpoint is admin-only', '[Authorize(Roles = "admin")]' in medical)
check('extended and confidential data are not part of base EmployeeDto', 'PsychiatricFitnessCategory' not in (ROOT / 'Contracts/EmployeeDtos.cs').read_text(encoding='utf-8'))
check('leave records model supports planned and actual dates', all(x in entities for x in ['class EmployeeLeaveRecord', 'PlannedStart', 'PlannedEnd', 'ActualStart', 'ActualEnd']))
check('leave record APIs scope queries by employee and leave id', 'v.Id == leaveId && v.EmployeeId == employeeId' in leaves)
check('leave types and statuses are allow-listed', "r.LeaveType is not (\"main\" or \"veteran\" or \"additional\" or \"other\")" in leaves and "r.Status is not (\"planned\" or \"in-progress\" or \"completed\" or \"cancelled\")" in leaves)
check('entitlement values are configured rather than hard-coded legal assumptions', 'MainDays' in entitlement and 'VeteranDays' in entitlement and 'BasisNote' in entitlement)
check('priority months and blackout periods exist', 'class EmployeeLeavePreference' in entities and 'class LeaveBlackoutPeriod' in entities and 'class LeavePreferencesController' in planning)
check('birthday reminders added', 'birthdayWindowEnd' in alerts and 'NextBirthday' in alerts)
check('UMO reminder uses one calendar month', 'expiry.AddMonths(-1)' in alerts and '"umo"' in alerts)
check('SQL migration is transactional and non-destructive', 'BEGIN;' in sql and 'COMMIT;' in sql and 'DROP TABLE' not in sql.upper())
check('schema registration exists for new entities', all(x in context for x in ['DbSet<EmployeeExtendedData>', 'DbSet<EmployeeConfidentialHealthRecord>', 'DbSet<EmployeeLeaveRecord>', 'DbSet<EmployeeLeaveEntitlement>', 'DbSet<EmployeeLeavePreference>', 'DbSet<LeaveBlackoutPeriod>']))

for label, ok in checks:
    print(('PASS' if ok else 'FAIL') + ': ' + label)
print(f'\n{sum(ok for _, ok in checks)}/{len(checks)} static checks passed')
raise SystemExit(0 if all(ok for _, ok in checks) else 1)
