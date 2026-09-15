# Infinitix — Guía para completar Content Rating y Data Safety en Play Console

Estos dos formularios se llenan directamente en Play Console (no tienen API pública para
completarlos desde afuera), así que esto es una guía con las respuestas exactas para que
las cargues vos mismo. Están basadas en el código real del proyecto: no hay ads, no hay
analytics, no hay red, no hay cuentas — confirmado revisando `Scripts/`, `export_presets.cfg`
y el manifiesto de Android (sin SDKs de terceros, sin permisos declarados).

---

## 1. Content Rating (cuestionario IARC)

Play Console → tu app → Configuración de contenido → Clasificación de contenido.

**Categoría de la app:** Juego (Game).

Respuestas al cuestionario (la mayoría de las preguntas son "No" — Infinitix es un shooter
arcade geométrico, sin sangre, sin personajes realistas):

| Pregunta | Respuesta |
|---|---|
| Violencia — ¿hay representaciones de violencia? | Sí — violencia de fantasía leve. Los enemigos son formas geométricas abstractas (triángulos, cuadrados) que desaparecen al ser destruidos. Sin sangre, sin gore, sin personajes humanos/realistas como blanco. |
| Violencia sexual / desnudez | No |
| Lenguaje ofensivo / groserías | No |
| Contenido controlado (drogas, alcohol, tabaco) | No |
| Juego de azar simulado / apuestas | No (no hay loot boxes, no hay compras, no hay mecánicas de azar con dinero real o virtual comprable) |
| Contenido generado por el usuario compartido públicamente | No (la foto de personaje se guarda solo en el dispositivo, nunca se sube ni se comparte) |
| Interacción entre usuarios / chat | No (no hay multijugador, no hay red) |
| Comparte ubicación | No |
| Compras dentro de la app | No |
| Permite compartir información personal con otros usuarios | No |

**Clasificación esperada:** ESRB "Everyone" / PEGI 3 / equivalente más bajo — por la
violencia de fantasía mínima probablemente termine en PEGI 3 o 7 según el algoritmo IARC,
nunca más alto.

---

## 2. Data Safety (Seguridad de los datos)

Play Console → tu app → Configuración de contenido → Seguridad de los datos.

### Pregunta inicial
**¿Tu app recopila o comparte alguno de los tipos de datos de usuario requeridos?**
→ **No.**

Justificación: Infinitix no tiene conexión a internet, no tiene SDKs de analytics/ads/crash
reporting de terceros, no tiene sistema de cuentas, y no envía absolutamente ningún dato
fuera del dispositivo. Todo el progreso (nivel, monedas, stats, personaje creado) se guarda
localmente en el storage de la app (`user://`).

### Caso especial: la foto de personaje (Character Creator)
El juego sí accede a la galería de fotos del usuario (para que elija una imagen y crear su
piloto personalizado), pero:
- Se hace vía el selector nativo de Android (photo picker), sin pedir el permiso de
  almacenamiento — la app nunca tiene acceso general a la galería, solo a la foto puntual
  que el usuario elige.
- Esa imagen se procesa y guarda **únicamente en el dispositivo** (`user://`). Nunca se
  sube a ningún servidor, nunca se comparte, nunca sale de la app.

Según las reglas de Google, esto **no cuenta como "recopilación de datos"** en el sentido
del formulario, porque "recopilar" implica transmitir o guardar el dato fuera del
dispositivo del usuario, y ese no es el caso acá. Si Play Console te obliga a declarar
"Fotos" como tipo de dato al que accedés igual, marcá:
- Tipo de dato: **Fotos**
- ¿Se recopila?: **No** (se procesa en el dispositivo, no se transmite)
- ¿Se comparte con terceros?: **No**
- Finalidad: **Funcionalidad de la app** (personalización)
- ¿Es opcional?: **Sí** (el usuario puede jugar sin crear un personaje con foto propia)

### Seguridad de los datos (si el formulario pide igual estas secciones)
- ¿Los datos se transmiten cifrados?: N/A (no hay transmisión de datos)
- ¿Podés solicitar que se borren tus datos?: Sí — desinstalar la app borra todo, ya que no
  hay backend ni cuenta asociada.

---

## Resumen para copiar/pegar rápido en Play Console

- **Recopila datos de usuario:** No
- **Comparte datos con terceros:** No
- **Usa cifrado en tránsito:** No aplica (sin red)
- **Permite solicitar eliminación de datos:** Sí (desinstalación local)
- **Datos financieros, ubicación, contactos, mensajes, identificadores de dispositivo:** Ninguno recopilado ni compartido
