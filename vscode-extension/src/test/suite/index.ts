import * as path from 'path';
import Mocha from 'mocha';
import { glob } from 'glob';

const GrepVariable = 'MAXON_E2E_GREP';

function selectedBy(pattern: string): RegExp {
    try {
        return new RegExp(pattern);
    } catch (err) {
        throw new Error(`${GrepVariable} is not a valid regular expression: ${err instanceof Error ? err.message : err}`);
    }
}

export function run(): Promise<void> {
    // Create the mocha test
    const mocha = new Mocha({
        ui: 'tdd',
        color: true,
        timeout: 10000
    });

    const testsRoot = path.resolve(__dirname, '..');

    const grep = process.env[GrepVariable];

    return new Promise(async (resolve, reject) => {
        try {
            if (grep) {
                mocha.grep(selectedBy(grep));
            }

            // ⚠ SORTED, because the glob's own order is the filesystem's. A run whose order varies
            // between machines is a run whose failures cannot be reproduced from the report.
            const files = (await glob('**/**.test.js', { cwd: testsRoot })).sort();

            // Add files to the test suite
            files.forEach(f => mocha.addFile(path.resolve(testsRoot, f)));

            // Run the mocha test
            const runner = mocha.run((failures: number) => {
                // Mocha reports success for a run that selected nothing, which would pass a mistyped
                // pattern.
                if (grep && runner.total === 0) {
                    reject(new Error(`${GrepVariable}=${grep} selected no test.`));
                } else if (failures > 0) {
                    reject(new Error(`${failures} tests failed.`));
                } else {
                    resolve();
                }
            });
        } catch (err) {
            console.error(err);
            reject(err);
        }
    });
}
