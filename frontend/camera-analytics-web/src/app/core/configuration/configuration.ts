import { InjectionToken } from '@angular/core';

export interface Configuration {
  apiBaseUrl: string;
  signalREnabled: boolean;
  hubPath: string;
  refreshIntervalSeconds: number;
}

export const CONFIGURATION = new InjectionToken<Configuration>('Camera Analytics configuration');

export async function loadConfiguration(): Promise<Configuration> {
  const response = await fetch(new URL('config.json', document.baseURI), { cache: 'no-store' });
  if (!response.ok) throw new Error('Could not load application configuration.');
  const value = await response.json() as Partial<Configuration>;
  if (typeof value.apiBaseUrl !== 'string' || typeof value.signalREnabled !== 'boolean'
    || typeof value.hubPath !== 'string' || !value.hubPath.startsWith('/')
    || typeof value.refreshIntervalSeconds !== 'number' || value.refreshIntervalSeconds < 1) {
    throw new Error('Application configuration is invalid.');
  }
  return { apiBaseUrl: value.apiBaseUrl.replace(/\/$/, ''), signalREnabled: value.signalREnabled,
    hubPath: value.hubPath, refreshIntervalSeconds: value.refreshIntervalSeconds };
}
