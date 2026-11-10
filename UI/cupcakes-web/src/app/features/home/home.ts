import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-home',
  imports: [RouterLink],
  template: `
    <section class="intro-card">
      <p class="eyebrow">Feitos para adoçar seu dia</p>
      <h1>Loja de cupcakes gourmet</h1>
      <p class="intro-message">Nosso catálogo está sendo preparado com carinho.</p>
      @if (auth.user()) {
        <p>Seu acesso já está pronto. Em breve, nossos cupcakes estarão na vitrine.</p>
      } @else {
        <p>Enquanto isso, você já pode criar sua conta e deixar tudo pronto para escolher seus favoritos.</p>
        <div class="actions">
          <a class="button" routerLink="/cadastro">Criar minha conta</a>
          <a class="button secondary" routerLink="/entrar">Já tenho conta</a>
        </div>
      }
    </section>
  `,
  styles: `
    .intro-card {
      max-width: 44rem;
      margin: 3rem auto;
      padding: clamp(1.5rem, 5vw, 3.5rem);
      border: 1px solid #e1d6d2;
      border-radius: 1rem;
      background: #fffdfb;
    }

    h1 {
      font-size: clamp(2rem, 6vw, 3rem);
      line-height: 1.15;
      margin: 0 0 1.5rem;
      color: #452f2b;
    }

    p {
      line-height: 1.7;
    }

    .intro-message {
      font-size: 1.125rem;
    }
  `,
})
export class Home {
  protected readonly auth = inject(AuthService);
}
