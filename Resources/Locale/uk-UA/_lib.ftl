### Спеціальні повідомлення для внутрішніх функцій локалізатора.

# Використовується внутрішньою функцією PRESSURE().
# Frontier: PBa<PPa
zzzz-fmt-pressure = { TOSTRING($divided, "F1") } { $places ->
    [0] кПа
    [1] МПа
    [2] ГПа
    [3] ТПа
    [4] ППа
    *[5] ???
}

# Використовується внутрішньою функцією POWERWATTS().
zzzz-fmt-power-watts = { TOSTRING($divided, "F1") } { $places ->
    [0] Вт
    [1] кВт
    [2] МВт
    [3] ГВт
    [4] ТВт
    *[5] ???
}

# Використовується внутрішньою функцією POWERJOULES().
# Один джоуль дорівнює одному ват-секунді. Кіловат-година — це 3 600 000 джоулів (3,6 МДж).
zzzz-fmt-power-joules = { TOSTRING($divided, "F1") } { $places ->
    [0] Дж
    [1] кДж
    [2] МДж
    [3] ГДж
    [4] ТДж
    *[5] ???
}

# Використовується внутрішньою функцією ENERGYWATTHOURS().
zzzz-fmt-energy-watt-hours = { TOSTRING($divided, "F1") } { $places ->
    [0] Вт·год
    [1] кВт·год
    [2] МВт·год
    [3] ГВт·год
    [4] ТВт·год
    *[5] ???
}

# Використовується внутрішньою функцією PLAYTIME().
zzzz-fmt-playtime = {$hours} год {$minutes} хв
