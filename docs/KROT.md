# KRot / Крот

## Русская версия

KRot — мой экспериментальный VPN-протокол, вдохновлённый VLESS и AmneziaWG.
Я сделал его для личного VPN-сервера: в NORA можно установить узел на VPS,
создавать пользователей и отслеживать, сколько трафика они потребили.

В KRot я использую аутентифицированный TLS/TCP cover-профиль, шифрование
сеанса, проверку свежести bootstrap, защиту от повторов, возобновление сеанса
и временную защиту от IPv6-утечек на Windows. Статус `Connected` появляется
только после проверки DNS и HTTPS-трафика через туннель.

## English version

KRot is my experimental VPN protocol inspired by VLESS and AmneziaWG. I built
it for a personal VPN server: install a node on a VPS, create users, and keep
track of how much traffic they use from NORA.

In KRot I use an authenticated TLS/TCP cover profile, session encryption,
bootstrap freshness and replay checks, session resumption, and temporary
Windows IPv6-leak protection. `Connected` is shown only after DNS and HTTPS
traffic have been verified through the tunnel.
