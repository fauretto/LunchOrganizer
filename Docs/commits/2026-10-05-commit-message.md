Fixed a bug in the booking page where 'Book my lunches' saved only the first two of the selected days

Description:
- Booking page: each day booked or cancelled by 'Book my lunches' sent a change notification that this same page handled by reloading the week, and that reload replaced the selections still waiting to be saved with what was already in the database. The remaining days then looked unchanged and were skipped without any error, so only about two days of a five-day selection were booked. The save now works from a copy of the selections taken when it starts, and the page ignores change notifications while it is saving its own changes (also for 'Apply this menu to every open days'), since it reloads the week itself once the save is done. Changes made by other users are still picked up as before once the save has finished.
- Added logging of the save start and outcome (attempted, changed and failed days), of each day that fails to book or cancel, of the 'Apply this menu to every open days' result and of the notifications ignored during a save.
- Added unit tests for saving five days at once, for booking, changing and cancelling days in the same save, and for the live refresh after a save.

Author: Massimo Fauro

Version:
