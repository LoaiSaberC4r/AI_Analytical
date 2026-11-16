import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'ca-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="shell">
      <aside class="sidebar">
        <a class="brand" routerLink="/">
          <span class="brand-mark" aria-hidden="true"><i></i></span>
          <span>Camera<br><strong>Analytics</strong></span>
        </a>
        <nav aria-label="Main navigation">
          <a routerLink="/" routerLinkActive="selected" [routerLinkActiveOptions]="{exact:true}">Overview</a>
          <a routerLink="/live" routerLinkActive="selected">Live monitoring</a>
          <a routerLink="/analytics" routerLinkActive="selected">Analytics</a>
          <a routerLink="/sessions" routerLinkActive="selected">Sessions</a>
          <div class="nav-label">Management</div>
          <a routerLink="/sites" routerLinkActive="selected">Sites</a>
          <a routerLink="/cameras" routerLinkActive="selected">Cameras &amp; zones</a>
        </nav>
      </aside>
      <main id="main-content"><router-outlet /></main>
    </div>
  `,
})
export class App {}
