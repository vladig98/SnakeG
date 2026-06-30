// src/hooks/useSignalR.ts
import { useEffect, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import type { SimulationTickPayload } from '../types';

export function useSignalR(url: string) {
    const latestDataRef = useRef<SimulationTickPayload | null>(null);
    const [generation, setGeneration] = useState(1); // Add state for the UI

    useEffect(() => {
        const connection = new signalR.HubConnectionBuilder()
            .withUrl(url)
            .withAutomaticReconnect()
            .build();

        connection.on("ReceiveTick", (payload: SimulationTickPayload) => {
            latestDataRef.current = payload;

            // Only trigger a React re-render if the generation actually changed!
            setGeneration(prev => prev !== payload.generation ? payload.generation : prev);
        });

        connection.start().catch(err => console.error("SignalR Error: ", err));

        return () => {
            connection.stop();
        };
    }, [url]);

    // Return both the ref (for the canvas) and the state (for the UI)
    return { latestDataRef, generation };
}