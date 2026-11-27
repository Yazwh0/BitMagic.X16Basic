// Copies x16basic, the command line tool (src/BitMagic.X16Basic.Tool), into tool/, where the extension runs it
// from to save and open .PRG files.
const fs = require('fs');
const path = require('path');

const configuration = process.env.CONFIGURATION || 'Debug';
const from = path.join(__dirname, '..', '..', 'src', 'BitMagic.X16Basic.Tool', 'bin', configuration, 'net10.0');
const to = path.join(__dirname, '..', 'tool');

if (!fs.existsSync(path.join(from, 'x16basic.dll'))) {
    console.error(`Build the tool first, nothing in ${from}`);
    process.exit(1);
}

fs.rmSync(to, { recursive: true, force: true });
fs.mkdirSync(to, { recursive: true });

for (const file of fs.readdirSync(from)) {
    if (/^(x16basic\.(dll|runtimeconfig\.json|deps\.json)|BitMagic\.X16Basic\.dll)$/.test(file)) {
        fs.copyFileSync(path.join(from, file), path.join(to, file));
        console.log(`tool/${file}`);
    }
}
