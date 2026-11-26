// Copies the BASIC debug mode into debugmode/, where the extension tells X16D to load it from.
// Only our own assemblies: X16D provides BitMagic's.
const fs = require('fs');
const path = require('path');

const configuration = process.env.CONFIGURATION || 'Debug';
const from = path.join(__dirname, '..', '..', 'src', 'BitMagic.X16Basic.Debugger', 'bin', 'x64', configuration, 'net10.0');
const to = path.join(__dirname, '..', 'debugmode');

if (!fs.existsSync(path.join(from, 'BitMagic.X16Basic.Debugger.dll'))) {
    console.error(`Build the debug mode first, nothing in ${from}`);
    process.exit(1);
}

fs.rmSync(to, { recursive: true, force: true });
fs.mkdirSync(to, { recursive: true });

for (const file of fs.readdirSync(from)) {
    if (/^BitMagic\.X16Basic(\.Debugger)?\.(dll|pdb)$/.test(file)) {
        fs.copyFileSync(path.join(from, file), path.join(to, file));
        console.log(`debugmode/${file}`);
    }
}
