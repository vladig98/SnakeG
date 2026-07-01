import { useState } from 'react';
import { useSignalR } from './hooks/useSignalR';
import CanvasGrid100 from './components/CanvasGrid100';
import SingleBestView from './components/SingleBestView';

export default function App() {
    const [viewMode, setViewMode] = useState<'grid100' | 'best'>('grid100');
    const [simCount, setSimCount] = useState<number>(1);

    // Make sure this matches your .NET HTTPS port!
    const { latestDataRef, generation } = useSignalR('https://localhost:7135/snake');

    const handleSimulate = async () => {
        try {
            await fetch(`https://localhost:7135/api/simulate/${simCount}`, {
                method: 'POST',
            });
        } catch (error) {
            console.error("Failed to trigger simulation. Is the backend running?", error);
        }
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', minHeight: '100vh', backgroundColor: '#030712' }}>

            {/* Sleek Dark Header */}
            <header style={{
                backgroundColor: '#111827',
                padding: '1rem 2rem',
                borderBottom: '1px solid #1f2937',
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                boxShadow: '0 4px 6px -1px rgba(0, 0, 0, 0.3)'
            }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '15px' }}>
                    {/* Pulsing neon green status dot */}
                    <div style={{ width: '12px', height: '12px', backgroundColor: '#10b981', borderRadius: '50%', boxShadow: '0 0 10px #10b981' }}></div>
                    <h1 style={{ margin: 0, fontSize: '1.5rem', fontWeight: 'bold', color: '#f3f4f6', letterSpacing: '1px' }}>
                        GA Snake Training
                    </h1>

                    <span style={{
                        marginLeft: '10px',
                        padding: '4px 10px',
                        backgroundColor: '#1f2937',
                        border: '1px solid #374151',
                        borderRadius: '6px',
                        color: '#9ca3af',
                        fontWeight: '600',
                        fontFamily: 'monospace',
                        fontSize: '1.1rem'
                    }}>
                        GEN {generation}
                    </span>
                </div>

                {/* Right Side Controls */}
                <div style={{ display: 'flex', gap: '15px', alignItems: 'center' }}>

                    {/* Simulation Controller */}
                    <div style={{ display: 'flex', alignItems: 'center', backgroundColor: '#1f2937', borderRadius: '6px', overflow: 'hidden', border: '1px solid #374151' }}>
                        <input
                            type="number"
                            min="1"
                            value={simCount}
                            onChange={(e) => setSimCount(parseInt(e.target.value) || 1)}
                            style={{
                                width: '60px',
                                padding: '10px',
                                backgroundColor: 'transparent',
                                border: 'none',
                                color: '#f3f4f6',
                                outline: 'none',
                                textAlign: 'center',
                                fontWeight: 'bold'
                            }}
                        />
                        <button
                            onClick={handleSimulate}
                            style={{
                                padding: '10px 20px',
                                backgroundColor: '#10b981', // Neon green action button
                                color: '#042f2e',
                                border: 'none',
                                cursor: 'pointer',
                                fontWeight: 'bold',
                                transition: 'background-color 0.2s'
                            }}
                            onMouseOver={(e) => e.currentTarget.style.backgroundColor = '#34d399'}
                            onMouseOut={(e) => e.currentTarget.style.backgroundColor = '#10b981'}
                        >
                            Simulate
                        </button>
                    </div>

                    {/* View Toggle */}
                    <button
                        onClick={() => setViewMode(viewMode === 'grid100' ? 'best' : 'grid100')}
                        style={{
                            padding: '10px 20px',
                            backgroundColor: '#3b82f6',
                            color: 'white',
                            border: 'none',
                            borderRadius: '6px',
                            cursor: 'pointer',
                            fontWeight: '600',
                            transition: 'all 0.2s'
                        }}
                    >
                        View: {viewMode === 'grid100' ? '100 Screens' : 'Best AI Only'}
                    </button>
                </div>
            </header>

            {/* Main Canvas Container */}
            <main style={{ flex: 1, padding: '2rem', display: 'flex', justifyContent: 'center', alignItems: 'center' }}>
                <div style={{
                    width: '100%',
                    maxWidth: '1800px',
                    backgroundColor: '#111827',
                    borderRadius: '12px',
                    padding: '1rem',
                    border: '1px solid #1f2937',
                    boxShadow: '0 10px 25px -5px rgba(0,0,0,0.5)'
                }}>
                    {viewMode === 'grid100' ? (
                        <CanvasGrid100 latestDataRef={latestDataRef} />
                    ) : (
                        <SingleBestView latestDataRef={latestDataRef} />
                    )}
                </div>
            </main>

        </div>
    );
}