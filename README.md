<p align="center"><img src="assets/icon.png" width="96" alt=""></p>

<h1 align="center">NTFS Permissions Reporter</h1>

<p align="center">Auditoría rápida de permisos NTFS y de recursos compartidos en Windows.<br>
Un único <code>.exe</code> sin instalación, gratuito y de código abierto.</p>

<p align="center"><a href="../../releases/latest"><b>⬇ Descargar la última versión</b></a></p>

---

## Qué hace

**Informe de permisos.** Recorre las carpetas que le indiques y lista cada entrada de permisos: quién, qué permiso, si es permitir o denegar, a qué se aplica, si es heredado…

**Acceso efectivo de un usuario.** Calcula qué acceso *real* tiene un usuario en cada carpeta. Tiene en cuenta:

- todos sus grupos, incluidos los anidados;
- las denegaciones;
- los grupos locales del servidor;
- los permisos del recurso compartido.

### Añadir directorios

- **Ruta:** escribe o pega rutas, una por línea (`D:\Datos`, `\\servidor\recurso\carpeta`).
- **Shares:** escribe el nombre de un servidor y marca uno o varios recursos compartidos.
- **Importar:** carga un `.txt` con una ruta por línea. Las líneas que empiezan por `#` se ignoran.
- **Arrastrar:** suelta carpetas del Explorador sobre la lista, o pega rutas con `Ctrl+V`.

### Ver los resultados

- **Tabla:** todas las entradas en una tabla que se puede ordenar. Eliges qué columnas ver y reordenarlas arrastrando la cabecera.
- **Árbol:** el árbol de carpetas a la izquierda y los permisos de la carpeta seleccionada a la derecha. El icono indica si la carpeta tiene permisos explícitos, herencia deshabilitada o error.

### Filtros combinables

Añade tantas condiciones como quieras, con el formato **campo · operador · valor**, y elige si deben cumplirse **todas** (Y) o **alguna** (O).

Operadores disponibles: *Empieza por, Contiene, No empieza por, No contiene, No termina en, Termina en, Es, No es*.

| Campo | Campo en NTFS Reports |
|---|---|
| Cuenta deshabilitada | Account disabled |
| Nombre de cuenta | Account name |
| SID de la cuenta | Account SID |
| Tipo de cuenta | Account type |
| Se aplica a | Apply To |
| Lista completa de permisos del directorio | Directory full permissions list |
| El directorio hereda permisos | Directory inherits permissions |
| Dominio (DNS) | Domain name (DNS) |
| Dominio (NetBIOS) | Domain name (NetBIOS) |
| SID del dominio | Domain SID |
| Grupo sin miembros | Group contains no members |
| Ámbito del grupo | Group Scope |
| Permiso (avanzado) | Permission (advanced) |
| Permiso (simple) | Permission (simple) |
| Tipo de acceso | Permission access type |
| Permiso heredado | Permission is inherited |
| Permisos idénticos al directorio padre | Permissions are identical to parent directory |
| SID sin resolver | SID cannot be resolved to account name |

Además están disponibles *Directorio*, *Propietario*, *Nivel* y *Error*.

El desplegable de valor sugiere los valores que aparecen en los resultados. También puedes hacer clic derecho en cualquier celda y elegir **Filtrar: mostrar solo este valor** o **excluir este valor**.

### Exportar

Formatos disponibles: **Excel (.xlsx)**, **CSV** (con `;` para Excel en español o con `,`), **texto tabulado**, **HTML** (con buscador), **JSON** y **XML**.

Puedes exportar solo las filas filtradas o todas, y elegir exactamente qué columnas incluir.

## Uso

1. Descarga `NtfsPermissionsReporter.exe` desde [Releases](../../releases/latest).
2. Ejecútalo **como administrador** del servidor de ficheros. Así lee incluso carpetas a las que el administrador no tiene acceso, porque activa el privilegio de copia de seguridad, igual que `robocopy /B`. Si lo abres sin permisos, el botón de la cabecera lo reinicia como administrador.
3. Añade directorios y pulsa **Analizar** (`F5`).

> Si Windows SmartScreen muestra un aviso, pulsa *Más información → Ejecutar de todas formas*. Aparece porque el ejecutable no está firmado digitalmente.

> Ejecutado como administrador, Windows no muestra las unidades de red mapeadas (H:, G:…). Usa siempre la ruta `\\servidor\recurso`.

Atajos de teclado: `F5` analizar o detener · `Ctrl+F` buscar · `Ctrl+L` filtros · `Ctrl+E` exportar · `Supr` quitar directorios de la lista.

Requisitos: Windows 10/11 o Windows Server 2012 R2 o posterior, con .NET Framework 4.5 o superior (viene incluido en Windows).

## Por qué es rápido

- Recorre las carpetas con muchos hilos en paralelo (32 por defecto, configurable). En red el cuello de botella es la latencia, y el paralelismo la oculta.
- Usa `FindFirstFileEx` con *LargeFetch* y sin nombres 8.3, y solo lee el propietario y la DACL de cada carpeta.
- Evalúa las ACL directamente sobre los bytes del descriptor de seguridad, sin objetos intermedios.
- Resuelve cada cuenta una sola vez y la guarda en caché. Los grupos del usuario los calcula el controlador de dominio con `tokenGroups`, en una sola consulta.
- Las tablas son virtuales: muestran millones de filas sin cargarlas en pantalla.
- Soporta rutas de más de 260 caracteres.

## Compilar desde el código

No hace falta Visual Studio: haz doble clic en **`build.bat`**. Usa el `csc.exe` que viene con Windows y genera `dist\NtfsPermissionsReporter.exe`.

Cada vez que se sube una etiqueta `v*` (por ejemplo `v1.0.1`), GitHub Actions compila el ejecutable y lo publica en *Releases*. Con cada push a `main` lo deja como artefacto descargable en la pestaña *Actions*.

```
src/
  Native.cs        P/Invoke (ficheros, seguridad, NetAPI)
  Security.cs      rutas, descriptores de seguridad, textos de permisos
  Accounts.cs      resolución de cuentas (AD y locales), shares, acceso efectivo
  Model.cs         modelo del informe, campos, recorrido paralelo
  Filters.cs       reglas de filtro
  Export.cs        exportadores (xlsx, csv, txt, html, json, xml)
  Theme.cs         estilo visual y controles propios
  Dialogs.cs       añadir rutas, shares de servidor, exportar
  MainForm*.cs     ventana principal (informe, árbol, acceso efectivo)
```

## Limitaciones

- Analiza carpetas, no archivos individuales.
- *Grupo sin miembros*, *Cuenta deshabilitada*, *Ámbito* y *Dominio (DNS)* se obtienen de Active Directory. Si el equipo no está en un dominio, esas columnas quedan vacías para las cuentas de dominio.
- El acceso efectivo no evalúa ACE condicionales (Control de Acceso Dinámico).
