### Команди голосування

## Команда createvote
cmd-createvote-desc = Створити голосування.
cmd-createvote-help = Використання: createvote <'restart'|'preset'|'map'>
cmd-createvote-cannot-call-vote-now = Зараз не можна розпочати голосування!
cmd-createvote-invalid-vote-type = Некоректний тип голосування.
cmd-createvote-arg-vote-type = <vote type>

## Команда customvote
cmd-customvote-desc = Створити власне голосування.
cmd-customvote-help = Використання: customvote <title> <option1> <option2> [option3...]
cmd-customvote-on-finished-tie = Голосування «{$title}» завершилося нічиєю між варіантами: {$ties}!
cmd-customvote-on-finished-win = Голосування «{$title}» завершилося. Переміг варіант: {$winner}!
cmd-customvote-arg-title = <title>
cmd-customvote-arg-option-n = <option{ $n }>

## Команда vote
cmd-vote-desc = Проголосувати в активному голосуванні.
cmd-vote-help = Використання: vote <voteId> <option>
cmd-vote-cannot-call-vote-now = Зараз не можна розпочати голосування!
cmd-vote-on-execute-error-must-be-player = Потрібно бути гравцем.
cmd-vote-on-execute-error-invalid-vote-id = Некоректний ID голосування.
cmd-vote-on-execute-error-invalid-vote-options = Некоректні варіанти голосування.
cmd-vote-on-execute-error-invalid-vote = Некоректне голосування.
cmd-vote-on-execute-error-invalid-option = Некоректний варіант.

## Команда listvotes
cmd-listvotes-desc = Показати активні голосування.
cmd-listvotes-help = Використання: listvotes

## Команда cancelvote
cmd-cancelvote-desc = Скасувати активне голосування.
cmd-cancelvote-help = Використання: cancelvote <id>
                      ID можна дізнатися командою listvotes.
cmd-cancelvote-error-invalid-vote-id = Некоректний ID голосування.
cmd-cancelvote-error-missing-vote-id = Не вказано ID.
cmd-cancelvote-arg-id = <id>
