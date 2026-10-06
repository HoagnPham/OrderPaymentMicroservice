import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(process.argv[2]));
for (const name of ["YeuCauChucNang", "YeuCauPhiChucNang", "RangBuoc", "MoHinhTrangThai", "ThuocTinhAnToan", "LoaiTruLanNhau", "HamSinhMa", "KiemTraSoHoc", "TestCase"]) {
  const sheet = workbook.worksheets.getItem(name);
  console.log(`### ${name}`);
  console.log(JSON.stringify(sheet.getUsedRange().values));
}
