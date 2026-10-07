import fs from "node:fs/promises";
import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const inputPath = "../OrderPaymentMicroservice_Security_Spec.xlsx";
const input = await FileBlob.load(inputPath);
const workbook = await SpreadsheetFile.importXlsx(input);

const sheets = await workbook.inspect({
  kind: "workbook,sheet,table,definedName,drawing",
  maxChars: 20000,
  tableMaxRows: 12,
  tableMaxCols: 14,
  tableMaxCellChars: 160,
});
console.log(sheets.ndjson);

await fs.mkdir("previews", { recursive: true });
for (const sheet of workbook.worksheets.items) {
  const region = await workbook.inspect({
    kind: "region,formula",
    sheetId: sheet.name,
    range: sheet.getUsedRange()?.address ?? "A1:Z100",
    maxChars: 15000,
    options: { maxResults: 200 },
  });
  console.log(`\n=== ${sheet.name} ===`);
  console.log(region.ndjson);

  const preview = await workbook.render({
    sheetName: sheet.name,
    autoCrop: "all",
    scale: 1,
    format: "png",
  });
  const safeName = sheet.name.replace(/[^a-zA-Z0-9_-]+/g, "_");
  await fs.writeFile(`previews/${safeName}.png`, new Uint8Array(await preview.arrayBuffer()));
}
