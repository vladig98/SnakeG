import { useState, useRef } from 'react';
import { useSignalR } from './hooks/useSignalR';
import CanvasGrid100 from './components/CanvasGrid100';
import SingleBestView from './components/SingleBestView';

export default function App() {
    const [viewMode, setViewMode] = useState<'grid100' | 'best'>('grid100');
    const [simCount, setSimCount] = useState<number>(1);
    const [isSaving, setIsSaving] = useState(false);

    // Make sure this matches your .NET HTTPS port!
    const { latestDataRef, generation } = useSignalR('https://localhost:7135/snake');

    // --- NEW: File Input Reference for importing brains ---
    const fileInputRef = useRef<HTMLInputElement>(null);

    const [settings, setSettings] = useState({
        mutationRate: 0.05,
        tournamentSize: 5,
        elitismCount: 3,
        numberOfParents: 10,
        eatenApplePoints: 10000,
        extraApplesMultiplier: 5000,
        rightDirectionPoints: 1,
        wrongDirectionPoints: 0,
        pointForLooping: -100,
        deathPenalty: -10000,
        numberOfRepeats: 3,
        healthOffset: 50
    });

    const handleSimulate = async () => {
        try {
            await fetch(`https://localhost:7135/api/simulate/${simCount}`, {
                method: 'POST',
            });
        } catch (error) {
            console.error("Failed to trigger simulation. Is the backend running?", error);
        }
    };

    const handleApplySettings = async () => {
        setIsSaving(true);
        try {
            await fetch('https://localhost:7135/api/settings', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(settings)
            });
        } catch (error) {
            console.error("Failed to update settings.", error);
        } finally {
            setIsSaving(false);
        }
    };

    const handleSettingChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const { name, value, type } = e.target;
        setSettings(prev => ({
            ...prev,
            [name]: type === 'number' ? Number(value) : value
        }));
    };

    // --- NEW: Export Handler ---
    const handleExport = async () => {
        try {
            const res = await fetch('https://localhost:7135/api/brain/export');
            if (!res.ok) {
                alert("No champion available to export yet. Wait for Gen 1 to finish!");
                return;
            }
            const brain = await res.json();

            // Create a downloadable JSON file in the browser
            const blob = new Blob([JSON.stringify(brain, null, 2)], { type: 'application/json' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `champion-brain-gen-${generation}.json`;
            a.click();
            URL.revokeObjectURL(url);
        } catch (error) {
            console.error("Export failed", error);
        }
    };

    // --- NEW: Import Handler ---
    const handleImport = async (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (!file) return;

        try {
            const text = await file.text();
            const brain = JSON.parse(text); // Validate it is actual JSON

            const res = await fetch('https://localhost:7135/api/brain/import', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(brain)
            });

            if (res.ok) {
                alert("Brain successfully injected! It will spawn at the start of the next generation.");
            }
        } catch (error) {
            alert("Failed to parse or upload the brain file. Make sure it is a valid JSON.");
            console.error(error);
        }

        // Reset the input so you can upload the exact same file again if you want
        if (fileInputRef.current) fileInputRef.current.value = '';
    };

    return (
        <div style={{ display: 'flex', flexDirection: 'column', minHeight: '100vh', backgroundColor: '#030712', color: '#f3f4f6' }}>

            {/* Sleek Dark Header */}
            <header style={{
                backgroundColor: '#111827',
                padding: '1rem 2rem',
                borderBottom: '1px solid #1f2937',
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                boxShadow: '0 4px 6px -1px rgba(0, 0, 0, 0.3)',
                zIndex: 10
            }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '15px' }}>
                    <div style={{ width: '12px', height: '12px', backgroundColor: '#10b981', borderRadius: '50%', boxShadow: '0 0 10px #10b981' }}></div>
                    <h1 style={{ margin: 0, fontSize: '1.5rem', fontWeight: 'bold', color: '#f3f4f6', letterSpacing: '1px' }}>
                        GA Snake Training
                    </h1>

                    <span style={{
                        marginLeft: '10px', padding: '4px 10px', backgroundColor: '#1f2937',
                        border: '1px solid #374151', borderRadius: '6px', color: '#9ca3af',
                        fontWeight: '600', fontFamily: 'monospace', fontSize: '1.1rem'
                    }}>
                        GEN {generation}
                    </span>
                </div>

                {/* Right Side Controls */}
                <div style={{ display: 'flex', gap: '15px', alignItems: 'center' }}>
                    <div style={{ display: 'flex', alignItems: 'center', backgroundColor: '#1f2937', borderRadius: '6px', overflow: 'hidden', border: '1px solid #374151' }}>
                        <input
                            type="number" min="1" value={simCount}
                            onChange={(e) => setSimCount(parseInt(e.target.value) || 1)}
                            style={{ width: '60px', padding: '10px', backgroundColor: 'transparent', border: 'none', color: '#f3f4f6', outline: 'none', textAlign: 'center', fontWeight: 'bold' }}
                        />
                        <button
                            onClick={handleSimulate}
                            style={{ padding: '10px 20px', backgroundColor: '#10b981', color: '#042f2e', border: 'none', cursor: 'pointer', fontWeight: 'bold', transition: 'background-color 0.2s' }}
                            onMouseOver={(e) => e.currentTarget.style.backgroundColor = '#34d399'}
                            onMouseOut={(e) => e.currentTarget.style.backgroundColor = '#10b981'}
                        >
                            Simulate
                        </button>
                    </div>

                    <button
                        onClick={() => setViewMode(viewMode === 'grid100' ? 'best' : 'grid100')}
                        style={{ padding: '10px 20px', backgroundColor: '#3b82f6', color: 'white', border: 'none', borderRadius: '6px', cursor: 'pointer', fontWeight: '600', transition: 'all 0.2s' }}
                    >
                        View: {viewMode === 'grid100' ? '100 Screens' : 'Best AI Only'}
                    </button>
                </div>
            </header>

            {/* Layout Wrapper */}
            <div style={{ display: 'flex', flex: 1, overflow: 'hidden' }}>

                {/* Sidebar Settings Dashboard */}
                <aside style={{
                    width: '320px', backgroundColor: '#111827', borderRight: '1px solid #1f2937',
                    padding: '1.5rem', display: 'flex', flexDirection: 'column', gap: '1.5rem', overflowY: 'auto'
                }}>
                    <h2 style={{ fontSize: '1.2rem', margin: 0, color: '#60a5fa', borderBottom: '1px solid #1f2937', paddingBottom: '10px' }}>Hyperparameters</h2>

                    {/* --- NEW: Data Management Group --- */}
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '10px', paddingBottom: '15px', borderBottom: '1px solid #1f2937' }}>
                        <h3 style={{ fontSize: '0.9rem', color: '#9ca3af', textTransform: 'uppercase', letterSpacing: '1px', margin: 0 }}>Save / Load AI</h3>

                        <div style={{ display: 'flex', gap: '10px' }}>
                            <button
                                onClick={handleExport}
                                style={{ flex: 1, padding: '8px', backgroundColor: '#374151', color: '#10b981', border: '1px solid #4b5563', borderRadius: '6px', cursor: 'pointer', fontWeight: 'bold' }}
                            >
                                💾 Export Best
                            </button>

                            <button
                                onClick={() => fileInputRef.current?.click()}
                                style={{ flex: 1, padding: '8px', backgroundColor: '#374151', color: '#60a5fa', border: '1px solid #4b5563', borderRadius: '6px', cursor: 'pointer', fontWeight: 'bold' }}
                            >
                                📂 Import AI
                            </button>

                            {/* Hidden file input used by the Import button */}
                            <input
                                type="file"
                                accept=".json"
                                ref={fileInputRef}
                                onChange={handleImport}
                                style={{ display: 'none' }}
                            />
                        </div>
                    </div>

                    {/* Genetic Group */}
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                        <h3 style={{ fontSize: '0.9rem', color: '#9ca3af', textTransform: 'uppercase', letterSpacing: '1px', margin: 0 }}>Genetics</h3>
                        <SettingInput label="Mutation Rate" name="mutationRate" value={settings.mutationRate} step="0.01" onChange={handleSettingChange} />
                        <SettingInput label="Tournament Size" name="tournamentSize" value={settings.tournamentSize} onChange={handleSettingChange} />
                        <SettingInput label="Elites Preserved" name="elitismCount" value={settings.elitismCount} onChange={handleSettingChange} />
                        <SettingInput label="Breeding Parents" name="numberOfParents" value={settings.numberOfParents} onChange={handleSettingChange} />
                    </div>

                    {/* Environment Group */}
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
                        <h3 style={{ fontSize: '0.9rem', color: '#9ca3af', textTransform: 'uppercase', letterSpacing: '1px', margin: 0 }}>Environment & Scoring</h3>
                        <SettingInput label="Apple Points" name="eatenApplePoints" value={settings.eatenApplePoints} step="1000" onChange={handleSettingChange} />
                        <SettingInput label="Extra Apple Multiplier" name="extraApplesMultiplier" value={settings.extraApplesMultiplier} step="1000" onChange={handleSettingChange} />
                        <SettingInput label="Right Dir Points" name="rightDirectionPoints" value={settings.rightDirectionPoints} onChange={handleSettingChange} />
                        <SettingInput label="Wrong Dir Points" name="wrongDirectionPoints" value={settings.wrongDirectionPoints} onChange={handleSettingChange} />
                        <SettingInput label="Looping Penalty" name="pointForLooping" value={settings.pointForLooping} step="100" onChange={handleSettingChange} />
                        <SettingInput label="Death Penalty" name="deathPenalty" value={settings.deathPenalty} step="1000" onChange={handleSettingChange} />
                        <SettingInput label="Loop Repeats Max" name="numberOfRepeats" value={settings.numberOfRepeats} onChange={handleSettingChange} />
                        <SettingInput label="BFS Health Offset" name="healthOffset" value={settings.healthOffset} step="10" onChange={handleSettingChange} />
                    </div>

                    <button
                        onClick={handleApplySettings}
                        style={{
                            marginTop: 'auto', padding: '12px', backgroundColor: isSaving ? '#4b5563' : '#3b82f6', color: 'white',
                            border: 'none', borderRadius: '6px', cursor: isSaving ? 'not-allowed' : 'pointer', fontWeight: 'bold',
                            transition: 'all 0.2s', boxShadow: isSaving ? 'none' : '0 0 15px rgba(59, 130, 246, 0.4)'
                        }}
                    >
                        {isSaving ? 'Applying...' : 'Apply Rules To Next Gen'}
                    </button>
                </aside>

                {/* Main Canvas Container */}
                <main style={{ flex: 1, padding: '2rem', display: 'flex', justifyContent: 'center', alignItems: 'center', overflow: 'hidden' }}>
                    <div style={{
                        width: '100%', maxWidth: '1800px', backgroundColor: '#111827',
                        borderRadius: '12px', padding: '1rem', border: '1px solid #1f2937',
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
        </div>
    );
}

// Helper component for clean input rendering
function SettingInput({ label, name, value, onChange, step = "1" }: { label: string, name: string, value: number, onChange: any, step?: string }) {
    return (
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', backgroundColor: '#1f2937', padding: '8px 12px', borderRadius: '6px', border: '1px solid #374151' }}>
            <label style={{ fontSize: '0.85rem', color: '#d1d5db', fontWeight: '500' }}>{label}</label>
            <input
                type="number"
                name={name}
                value={value}
                step={step}
                onChange={onChange}
                style={{
                    width: '80px', padding: '4px 8px', backgroundColor: '#111827', border: '1px solid #4b5563',
                    color: '#10b981', borderRadius: '4px', outline: 'none', textAlign: 'right', fontWeight: 'bold',
                    fontFamily: 'monospace'
                }}
            />
        </div>
    );
}