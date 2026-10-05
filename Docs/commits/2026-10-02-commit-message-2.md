Handled the import of the new monthly menu format and added a specific price per menu

Description:
- Menu import: the parser now also reads the monthly document format, where day cells carry a year ("LUNDI 05.10.26"), there is no header row and each cell starts with its own "MENU n" label. The label gives the menu number and is removed from the description, falling back to the header row and then to the column position. The previous weekly formats are parsed exactly as before. An import whose document year differs from the selected year is refused with the new MenuImportYearMismatch error (French and English messages), and nothing is imported.
- A price written at the end of a menu description (e.g. "14.80-") is extracted, removed from the description and stored as that menu's price.
- Added a nullable price column to menus (decimal(10,2), non-negative check constraint) with the AddMenuPrice migration. A booking's price is now the menu's price when set, otherwise the day price, otherwise the default lunch price, both for a single day and for "Apply this menu to every open days". Existing price snapshots are untouched.
- Booking page: a menu whose price differs from the day price shows it highlighted between brackets in its menu box, and in its accessible label.
- Administration "Menus & Prices" tab: each menu card has an optional price field (empty means the day price applies), and copying a menu to the whole week also copies its price.
- Added logging of the parsed document, the extracted prices, the price source used at booking time and menu price updates. Updated the user guide (English, French, Italian) for menu prices and the new import format, and added the session summary of 2 October 2026.

Author: Massimo Fauro

Version:
