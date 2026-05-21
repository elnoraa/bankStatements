import { useEffect, useRef } from 'react';
import * as signalR from '@microsoft/signalr';

export interface StatementStatusUpdate {
  statementId: string;
  status: string;
  parsedTransactionCount?: number;
  errorMessage?: string | null;
}

/**
 * Connects to the SignalR hub for real-time statement processing status updates.
 * Falls back gracefully if the connection fails (caller should handle polling fallback).
 *
 * @param accessToken - JWT access token for authentication
 * @param onStatusUpdate - Callback invoked when a status update is received
 * @param onConnected - Callback invoked when the connection is established
 * @param onDisconnected - Callback invoked when the connection is lost
 */
export function useStatementHub(
  accessToken: string | null,
  onStatusUpdate: (update: StatementStatusUpdate) => void,
  onConnected?: () => void,
  onDisconnected?: () => void,
) {
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  useEffect(() => {
    if (!accessToken) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/statement-processing', {
        accessTokenFactory: () => accessToken,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .build();

    connection.on('StatementStatusUpdated', (update: StatementStatusUpdate) => {
      onStatusUpdate(update);
    });

    connection.onreconnected(() => {
      onConnected?.();
    });

    connection.onclose(() => {
      onDisconnected?.();
    });

    connectionRef.current = connection;

    connection.start()
      .then(() => onConnected?.())
      .catch((err) => {
        console.warn('SignalR connection failed, falling back to polling', err);
        onDisconnected?.();
      });

    return () => {
      connection.stop().catch(() => {});
      connectionRef.current = null;
    };
  }, [accessToken]);
}
