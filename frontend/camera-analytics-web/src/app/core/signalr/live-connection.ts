import { DestroyRef, inject, Injectable, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { CONFIGURATION } from '../configuration/configuration';
import { CameraEvent, StatusEvent, SummaryEvent, ZoneEvent } from '../models/models';

export type LiveScope = { kind: 'Site' | 'Camera'; id: string };
type Callbacks = {
  camera: (event: CameraEvent) => void; zone: (event: ZoneEvent) => void; status: (event: StatusEvent) => void;
  summary: (event: SummaryEvent) => void; synchronized: () => void;
};

@Injectable()
export class LiveConnection {
  private readonly config = inject(CONFIGURATION);
  private stopConnection?: () => void;
  readonly state = signal<'Connected' | 'Connecting' | 'Reconnecting' | 'Disconnected' | 'Disabled'>('Disconnected');

  constructor() { inject(DestroyRef).onDestroy(() => this.stop()); }

  watch(scope: LiveScope, callbacks: Callbacks): void {
    this.stop();
    if (!this.config.signalREnabled) {
      this.state.set('Disabled');
      return;
    }

    const base = this.config.apiBaseUrl || location.origin;
    const connection = new HubConnectionBuilder().withUrl(base + this.config.hubPath)
      .withAutomaticReconnect().configureLogging(LogLevel.Error).build();
    let stopped = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const retry = () => {
      if (!stopped) timer = setTimeout(() => void start(), this.config.refreshIntervalSeconds * 1000);
    };
    const synchronize = async () => {
      await connection.invoke('SubscribeTo' + scope.kind, scope.id);
      if (!stopped) {
        this.state.set('Connected');
        callbacks.synchronized();
      }
    };
    const start = async () => {
      if (stopped || connection.state !== HubConnectionState.Disconnected) return;
      this.state.set('Connecting');
      try {
        await connection.start();
        if (!stopped) await synchronize();
      } catch {
        if (!stopped) {
          this.state.set('Disconnected');
          if (connection.state !== HubConnectionState.Disconnected) await connection.stop();
          else retry();
        }
      }
    };
    const dispatch = <T>(name: string, handler: (event: T) => void) =>
      connection.on(name, event => { if (!stopped) handler(event); });
    dispatch('CameraSnapshotUpdated', callbacks.camera);
    dispatch('ZoneMetricsUpdated', callbacks.zone);
    dispatch('CameraStatusChanged', callbacks.status);
    dispatch('AnalyticsSummaryUpdated', callbacks.summary);
    connection.onreconnecting(() => { if (!stopped) this.state.set('Reconnecting'); });
    connection.onreconnected(() => { if (!stopped) void this.resubscribe(connection, synchronize); });
    connection.onclose(() => { if (!stopped) { this.state.set('Disconnected'); retry(); } });
    this.stopConnection = () => {
      stopped = true;
      clearTimeout(timer);
      void connection.stop();
    };
    void start();
  }

  private async resubscribe(connection: HubConnection, synchronize: () => Promise<void>): Promise<void> {
    try { await synchronize(); }
    catch { await connection.stop(); }
  }

  stop(): void {
    this.stopConnection?.();
    this.stopConnection = undefined;
    this.state.set('Disconnected');
  }
}
