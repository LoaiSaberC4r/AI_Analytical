import { AbstractControl, FormControl, FormGroup, ValidationErrors, Validators } from '@angular/forms';

export type CatalogKind = 'sites' | 'cameras' | 'zones';

const segment = /^(?!\s)(?!.*\s$)[^/+#\u0000-\u001f\u007f]{1,128}$/;
const requiredText = Validators.pattern(/.*\S.*/);
const wholeCapacity = (control: AbstractControl): ValidationErrors | null =>
  control.value === null || Number.isInteger(control.value) ? null : { whole: true };
const thresholds = (control: AbstractControl): ValidationErrors | null => {
  const warning = control.get('warningThresholdPercent')?.value as number | null;
  const critical = control.get('criticalThresholdPercent')?.value as number | null;
  return warning !== null && critical !== null && warning >= critical ? { thresholds: true } : null;
};

export function catalogForm(kind: CatalogKind) {
  return new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, requiredText, Validators.maxLength(160)] }),
    description: new FormControl('', { nonNullable: true, validators: Validators.maxLength(1000) }),
    code: new FormControl('', { nonNullable: true, validators: kind === 'sites'
      ? [Validators.required, requiredText, Validators.maxLength(64)] : [] }),
    timeZoneId: new FormControl('', { nonNullable: true, validators: kind === 'sites'
      ? [Validators.required, requiredText, Validators.maxLength(128)] : [] }),
    siteId: new FormControl('', { nonNullable: true, validators: kind === 'cameras' ? Validators.required : [] }),
    externalId: new FormControl('', { nonNullable: true, validators: kind !== 'sites'
      ? [Validators.required, Validators.pattern(segment)] : [] }),
    capacity: new FormControl<number | null>(null, { validators: [Validators.min(1), Validators.max(2147483647), wholeCapacity] }),
    warningThresholdPercent: new FormControl<number | null>(null, { validators: Validators.min(0) }),
    criticalThresholdPercent: new FormControl<number | null>(null, { validators: Validators.min(0) }),
  }, { validators: thresholds });
}
