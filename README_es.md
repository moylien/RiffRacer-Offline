# Riff Racer - Parche Offline (Plug & Play)

Parche local sin servidores externos que restaura al 100% la creación de pistas personalizadas (**"Crear nueva pista"** / *New Track*) y la funcionalidad offline de **Riff Racer** (originalmente conocido como *Drive Any Track*).

---

## 1. ¿Qué soluciona este parche?

Cuando los servidores oficiales de Parse.com fueron dados de baja, el juego quedó inutilizado para crear pistas nuevas desde archivos de música propios, dejando el botón de "Crear nueva pista" sombreado e inactivo en el menú principal.

A diferencia de las soluciones comunitarias habituales (que requieren instalar Python, dependencias como Flask o levantar contenedores Docker en segundo plano), este parche es **totalmente nativo y autónomo**:
* No requiere servidores externos ni procesos secundarios.
* Corre dentro del mismo proceso del juego mediante DLLs reemplazadas.
* Es **Plug & Play**: cuenta con firma criptográfica completa válida en Windows (.NET Strong Name) para que cualquier usuario pueda instalarlo sin necesidad de permisos de administrador ni comandos.
* Mantiene intacto todo el progreso previo, coches desbloqueados, puntuaciones y perfiles (`Profile\Saves\save.bin`).

---

## 2. Instalación para jugadores (Fácil)

1. Ve a la carpeta donde tienes instalado el juego (donde se encuentra `Game.exe`).
2. Copia y pega los siguientes **3 archivos** en esa carpeta, reemplazando cuando Windows lo solicite:
   * `Parse.dll`
   * `Parse.NetFx45.dll`
   * `ParseOctane.dll`
3. Inicia el juego con `Game.exe`.
4. ¡Listo! La opción **"Crear nueva pista"** estará activa inmediatamente en el menú principal.

Los datos generados localmente (perfil del jugador, pistas convertidas, configuración de coste 0) se guardarán de forma automática y transparente en una subcarpeta llamada `LocalData\`.

---

## 3. Instalación en Batocera / Steam Deck / Linux (Wine / Proton)

1. Copia los mismos 3 archivos DLL dentro de la carpeta del juego en tu unidad de Batocera (por ejemplo, en `roms/windows/RiffRacer/` o tu prefijo de Wine).
2. **Recomendación para música:** Crea una carpeta llamada `Music` o `Canciones` dentro del directorio del juego y coloca ahí tus canciones (MP3, FLAC, WAV). Al pulsar "Crear nueva pista", el explorador de archivos del juego se abrirá dentro del directorio y podrás elegirlas directamente sin navegar por las carpetas del sistema Linux.
3. Asegúrate de que la carpeta del juego tenga permisos de escritura normales para que se cree y actualice `LocalData\`.

---

## 4. Repositorio de código y soporte

* **Repositorio oficial de GitHub:** [https://github.com/moylien/RiffRacer-Offline](https://github.com/moylien/RiffRacer-Offline)
* **Descarga de versiones empaquetadas (Releases):** [https://github.com/moylien/RiffRacer-Offline/releases](https://github.com/moylien/RiffRacer-Offline/releases)
