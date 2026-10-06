import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const files = process.argv.slice(2);
for (const file of files) {
  const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(file));
  const overview = await workbook.inspect({
    kind: "workbook,sheet,table",
    maxChars: 20000,
    tableMaxRows: 12,
    tableMaxCols: 12,
    tableMaxCellChars: 160,
  });
  console.log(`FILE\t${file}`);
  console.log(overview.ndjson);
  const sheets = await workbook.inspect({ kind: "sheet", include: "id,name", maxChars: 10000 });
  console.log("SHEETS");
  console.log(sheets.ndjson);
  for (const line of sheets.ndjson.split(/\r?\n/).filter(Boolean)) {
    let row;
    try { row = JSON.parse(line); } catch { continue; }
    const sheetName = row.name;
    if (!sheetName) continue;
    const region = await workbook.inspect({
      kind: "region",
      sheetId: sheetName,
      maxChars: 16000,
      tableMaxRows: 40,
      tableMaxCols: 20,
      tableMaxCellChars: 200,
    });
    console.log(`REGION\t${sheetName}`);
    console.log(region.ndjson);
  }
  for (const term of ["E2E", "End-to-End", "REQ-", "TEST-", "PASS", "FAIL", "TODO", "TBD", "N/A"]) {
    const matches = await workbook.inspect({
      kind: "match",
      searchTerm: term,
      options: { useRegex: false, maxResults: 100 },
      maxChars: 8000,
      summary: term,
    });
    console.log(`MATCH\t${term}`);
    console.log(matches.ndjson);
  }
}
