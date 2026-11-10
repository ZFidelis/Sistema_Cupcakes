import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

function passwordsMatch(control: AbstractControl): ValidationErrors | null {
  return control.get('password')?.value === control.get('confirmPassword')?.value
    ? null : { passwordsMismatch: true };
}

@Component({
  selector: 'app-auth-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './auth-page.html',
  styleUrl: './auth-page.css',
})
export class AuthPage {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly isRegister = inject(ActivatedRoute).snapshot.data['mode'] === 'register';
  protected readonly busy = signal(false);
  protected readonly submitted = signal(false);
  protected readonly registered = signal(false);
  protected readonly showPassword = signal(false);
  protected readonly error = signal('');
  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    password: ['', this.isRegister
      ? [Validators.required, Validators.minLength(8), Validators.maxLength(128)]
      : [Validators.required, Validators.maxLength(128)]],
    confirmPassword: ['', this.isRegister ? [Validators.required] : []],
  }, { validators: this.isRegister ? passwordsMatch : null });

  protected trimEmail(): void {
    this.form.controls.email.setValue(this.form.controls.email.value.trim());
  }

  protected async submit(): Promise<void> {
    if (this.busy()) return;
    this.trimEmail();
    this.submitted.set(true);
    this.error.set('');
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.busy.set(true);
    const { email, password } = this.form.getRawValue();
    try {
      if (this.isRegister) {
        await this.auth.register(email, password);
        this.form.reset();
        this.registered.set(true);
      } else {
        await this.auth.login(email, password);
        await this.router.navigateByUrl('/');
      }
    } catch (error) {
      this.error.set(this.errorMessage(error));
    } finally {
      this.busy.set(false);
    }
  }

  private errorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) return 'Não foi possível entrar. Confira e-mail e senha ou tente novamente mais tarde.';
      if (error.status === 409) return 'Este e-mail já está cadastrado. Entre na sua conta ou use outro e-mail.';
      if (error.status === 400 && error.error?.errors) {
        return this.isRegister
          ? 'Confira os dados. A senha precisa ter de 8 a 128 caracteres, com maiúscula, minúscula e número.'
          : 'Confira o e-mail e preencha a senha com até 128 caracteres.';
      }
      if (error.status === 400) return 'Não foi possível enviar. Atualize a página e tente novamente.';
      if (error.status === 0 || error.status >= 502) return 'Não foi possível conectar à loja. Tente novamente em instantes.';
    }
    return 'Não foi possível concluir. Tente novamente mais tarde.';
  }
}
