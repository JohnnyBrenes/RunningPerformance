// The OpenAPI code generator writes CRLF on Windows while .gitattributes asks
// for LF, which leaves every generated file looking modified in `git status`
// even though the normalised content is identical to the committed blob.
// Rewriting them here keeps `generate:api` idempotent on every platform.
import { readdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const generatedRoot = join(dirname(fileURLToPath(import.meta.url)), '..', 'src', 'api', 'generated')

async function* typescriptFiles(directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name)
    if (entry.isDirectory()) yield* typescriptFiles(path)
    else if (entry.name.endsWith('.ts')) yield path
  }
}

let normalized = 0
for await (const path of typescriptFiles(generatedRoot)) {
  const content = await readFile(path, 'utf8')
  if (!content.includes('\r\n')) continue
  await writeFile(path, content.replaceAll('\r\n', '\n'))
  normalized += 1
}

console.log(`normalize-generated-eol: ${normalized} file(s) rewritten to LF`)
