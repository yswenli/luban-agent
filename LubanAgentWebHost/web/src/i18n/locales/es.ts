import type { MessageKey } from './zh-CN';

/** Spanish messages; keys come from zh-CN. */
const es = {
  // ── Comunes ─────────────────────────────────────────────────────────────
  'common.add': 'Añadir',
  'common.cancel': 'Cancelar',
  'common.close': 'Cerrar',
  'common.confirm': 'Confirmar',
  'common.confirmDelete': 'Confirmar eliminación',
  'common.copy': 'Copiar',
  'common.delete': 'Eliminar',
  'common.download': 'Descargar',
  'common.loading': 'Cargando…',
  'common.rename': 'Renombrar',
  'common.retry': 'Reintentar',
  'common.save': 'Guardar',
  'common.saving': 'Guardando…',
  'common.status': 'Estado',
  'common.deleteNamed': 'Eliminar {name}',

  // ── Panel de ajustes (pages/Settings.tsx) ───────────────────────────────
  'settings.title': 'Ajustes',
  'settings.close': 'Cerrar ajustes (Esc)',
  'settings.search': 'Buscar ajustes',
  'settings.scope': 'Ámbito',
  'settings.scopeGlobal': 'Ámbito: ★ Global',
  'settings.scopeWorkspace': 'Ámbito: {name}',
  'settings.scopeNone': 'Ámbito: ningún espacio de trabajo seleccionado',
  'settings.globalOption': '★ Global',
  'settings.noWorkspace':
    'Aún no hay espacios de trabajo disponibles: cree o importe uno en «Proyectos» de la página de inicio.',
  'settings.noProviders':
    'La caché de modelos aún no está disponible; tras guardar el proveedor, pulse «Actualizar» en la pestaña «Modelos».',
  'settings.subtitleNotify': 'Se guarda en este navegador (no se sincroniza con la cuenta)',
  'settings.subtitleAccount':
    'Solo se guarda el hash de la contraseña; al cambiarla, las sesiones de otros dispositivos caducan al instante',
  'settings.subtitleUsers':
    'Solo visible para administradores: tras restablecerla, la contraseña anterior y la sesión del usuario caducan al instante',
  'settings.subtitleInvites':
    'Solo visible para administradores: el código de invitación es de un solo uso y caduca al registrarse',
  'settings.subtitleRoots':
    'Solo visible para administradores: lista blanca a nivel de máquina; todos los proyectos nuevos están sujetos a ella',
  'settings.subtitleData': 'Ámbito de las estadísticas: todas las conversaciones',
  'settings.subtitleGitCredentials': 'Solo HTTPS: se guardan por usuario y host del repositorio; las contraseñas están en texto plano',
  'settings.tab.account': 'Cuenta',
  'settings.tab.gitCredentials': 'Credenciales de Git',
  'settings.tab.invites': 'Invitaciones',
  'settings.tab.roots': 'Directorios raíz',
  'settings.tab.provider': 'Proveedores',
  'settings.tab.model': 'Modelos',
  'settings.tab.skill': 'Habilidades',
  'settings.tab.rule': 'Reglas',
  'settings.tab.mcp': 'Servidores MCP',
  'settings.tab.workspace': 'Espacio de trabajo',
  'settings.tab.notify': 'Notificaciones',
  'settings.tab.users': 'Usuarios',
  'settings.tab.data': 'Datos y uso',

  // ── Estadísticas de la base de conocimiento (lib/wiki.ts) ───────────────
  'wiki.summary.scanned': 'Analizados {count}',
  'wiki.summary.new': 'Nuevos {count}',
  'wiki.summary.updated': 'Actualizados {count}',
  'wiki.summary.skipped': 'Omitidos {count}',
  'wiki.summary.deleted': 'Eliminados {count}',
  'wiki.summary.chunks': 'Fragmentos {count}',
  'wiki.summary.embedded': 'Vectorizados {count}',

  // ── Plugins (lib/plugins.ts) ────────────────────────────────────────────
  'plugins.fileReadFailed': 'Error al leer el archivo',
  'plugins.state.enabled': 'Habilitado',
  'plugins.state.disabled': 'Deshabilitado',
  'plugins.state.error': 'Error',
  'plugins.state.installed': 'Instalado',

  // ── Errores de vista previa (lib/filePreview.ts) ─────────────────────────
  'filePreview.notFound':
    'No se encuentra este archivo en el espacio de trabajo; puede que se haya movido o eliminado.',
  'filePreview.tooLarge':
    'El archivo supera el límite de vista previa (2 MB para texto, 10 MB para imágenes); ábralo localmente.',
  'filePreview.unsupported':
    'Este formato no admite vista previa en la página; ábralo localmente.',
  'filePreview.readFailed':
    'Error de lectura: puede que otro programa esté usando el archivo o que falten permisos.',
  'filePreview.httpFailed': 'Error de vista previa (HTTP {status}).',

  // ── Errores de solicitud (lib/api.ts) ───────────────────────────────────
  'api.requestFailed': 'Error en la solicitud',
  'api.httpFailed': 'Error en la solicitud (HTTP {status})',

  // ── Carga de imágenes (lib/image.ts) ────────────────────────────────────
  'image.tooLarge':
    'La imagen tiene demasiados píxeles ({width}×{height}, máximo {max} megapíxeles); redúzcala antes de subirla',

  // ── Conexión (lib/sse.ts) ───────────────────────────────────────────────
  'sse.silentDrop':
    'La conexión con el servidor se interrumpió en silencio (sin datos durante mucho tiempo); reintentando la conexión',

  // ── Avisos (stores/app.ts) ──────────────────────────────────────────────
  'app.toast.projectCreated': 'Proyecto creado',
  'app.toast.projectRenamed': 'Proyecto renombrado',
  'app.toast.projectDeleted': 'Proyecto eliminado',
  'app.toast.renamed': 'Renombrado',
  'app.toast.sessionDeleted': 'Conversación eliminada',
  'app.toast.defaultModelSet': 'Establecido como modelo predeterminado de nuevas conversaciones',
  'app.toast.sessionModelSwitched': 'Modelo de esta conversación cambiado',
  'app.toast.modelsFailed': 'Error al obtener la lista de modelos: {message}',
  'app.modelUnselected': 'Ningún modelo seleccionado',
  'permission.defaultMode': 'Predeterminado (Default)',

  // ── Panel del espacio de trabajo (components/WorkspacePanel.tsx) ─────────
  'workspacePanel.readFailed':
    'Error de lectura: puede que el archivo se haya movido o eliminado, o que la cuenta actual no tenga permisos.',
  'workspacePanel.tooMany': 'Demasiados elementos; solo se muestra una parte inicial.',
  'workspacePanel.title': 'Espacio de trabajo',
  'workspacePanel.currentSession': 'Conversación actual',
  'workspacePanel.hideIgnored': 'Ocultar directorios como .git y node_modules',
  'workspacePanel.showIgnored': 'Mostrar directorios ignorados',
  'workspacePanel.close': 'Cerrar espacio de trabajo',
  'workspacePanel.filter': 'Filtrar archivos',
  'workspacePanel.clearFilter': 'Borrar filtro',
  'workspacePanel.files': 'Archivos del espacio de trabajo',
  'workspacePanel.noWorkspace': 'La conversación actual no tiene un espacio de trabajo vinculado.',
  'workspacePanel.selectFile': 'Seleccione un archivo a la izquierda para ver su contenido',

  // ── Lista de conversaciones (pages/SessionList.tsx) ─────────────────────
  'sessionList.today': 'Hoy',
  'sessionList.earlier': 'Anteriores',
  'sessionList.resizeLabel': 'Ajustar el ancho de la barra de proyectos',
  'sessionList.refresh': 'Actualizar conversaciones',
  'sessionList.loading': 'Cargando conversaciones…',
  'sessionList.emptyTitle': 'Aún no hay conversaciones',
  'sessionList.emptyBody':
    'Cree una conversación para empezar; se vinculará al espacio de trabajo actual.',
  'sessionList.emptyProjectTitle': 'Este proyecto aún no tiene conversaciones',
  'sessionList.emptyProjectBody': 'Cree una conversación; se vinculará al proyecto actual.',
  'sessionList.noMatchTitle': 'No hay conversaciones coincidentes',
  'sessionList.noMatchBody':
    'Pruebe con otra palabra clave o borre la búsqueda para ver todas las conversaciones.',
  'sessionList.renameTitle': 'Renombrar conversación',
  'sessionList.renameLabel': 'Título',
  'sessionList.deleteTitle': 'Eliminar conversación',
  'sessionList.deleteMessage':
    '¿Eliminar «{name}»? También se eliminarán los mensajes de esta conversación.',
  'sessionList.unknownProject': 'Proyecto desconocido',

  // ── Detalle del plugin (pages/plugins/PluginDetail.tsx) ─────────────────
  'plugins.zipRequired': 'Seleccione un paquete de plugin en formato .zip',
  'plugins.builtin': 'Integrado',
  'plugins.enable': 'Habilitar',
  'plugins.disable': 'Deshabilitar',
  'plugins.upgrade': 'Actualizar',
  'plugins.uninstall': 'Desinstalar',
  'plugins.upgradeUnsupported': 'Los plugins integrados no se pueden actualizar',
  'plugins.upgradeHint': 'Seleccione un zip de la nueva versión para sobrescribir la instalación',
  'plugins.uninstallUnsupported': 'Los plugins integrados no se pueden desinstalar',
  'plugins.uninstallHint': 'Eliminar el directorio del plugin y su registro de estado',
  'plugins.restartNotice':
    'Hay ensamblados aún sin liberar; los cambios surtirán efecto al reiniciar el host.',
  'plugins.lastError': 'Error en la última habilitación: {message}',
  'plugins.pageError':
    'Error al escribir la página reflejada; las habilidades y MCP no se ven afectados: {message}',
  'plugins.meta.id': 'Identificador',
  'plugins.meta.version': 'Versión',
  'plugins.meta.author': 'Autor',
  'plugins.meta.state': 'Estado',
  'plugins.skillsTitle': 'Habilidades ({count})',
  'plugins.noSkills': 'Este plugin no aporta habilidades.',
  'plugins.mcpTitle': 'Servidores MCP ({count})',
  'plugins.noMcp': 'Este plugin no aporta servidores MCP.',
  'plugins.pagesTitle': 'Entradas de página ({count})',
  'plugins.noPages':
    'Este plugin no incluye páginas propias; al habilitarlo no aparecerá en la barra lateral.',
  'plugins.availableAfterEnable': 'Disponible al habilitar',
  'plugins.open': 'Abrir',
  'plugins.note':
    'El directorio y el archivo de estado del plugin están aislados del cliente de escritorio. Las habilidades que aporta se escriben como archivos en el espacio de trabajo actual y también se verán en el escritorio al abrir ese mismo espacio; MCP solo funciona dentro de este proceso web.',
  'plugins.disableTitle': 'Deshabilitar plugin',
  'plugins.disableMessage':
    'Deshabilitar «{name}» anulará las habilidades y MCP que registra y quitará su entrada de página de la barra lateral.',
  'plugins.uninstallTitle': 'Desinstalar plugin',
  'plugins.uninstallMessage':
    'Se eliminarán el directorio del plugin «{name}» y su registro de estado. Esta acción no se puede deshacer. Si el host aún tiene ocupado el ensamblado, los archivos residuales se limpiarán automáticamente en el próximo arranque.',

  // ── Gestión de plugins (pages/plugins/PluginsOverlay.tsx) ───────────────
  'plugins.toast.upgraded': 'Actualización completada',
  'plugins.toast.installed': 'Plugin instalado y habilitado',
  'plugins.toast.enabled': 'Habilitado',
  'plugins.toast.disabled': 'Deshabilitado',
  'plugins.toast.uninstalled': 'Desinstalado',
  'plugins.toast.withWarnings': '{text} ({count} avisos; consulte el detalle)',
  'plugins.toast.withFirstWarning': '{text}: {message}',
  'plugins.title': 'Plugins',
  'plugins.refreshList': 'Actualizar la lista de plugins',
  'plugins.closeManager': 'Cerrar la gestión de plugins (Esc)',
  'plugins.installedCount': 'Instalados ({count})',
  'plugins.emptyList':
    'Aún no hay plugins. Los plugins instalados en la web se guardan por separado de los del escritorio; pulse «Instalar plugin» a la derecha para subir un paquete zip.',
  'plugins.selectEmpty': 'Seleccione un plugin a la izquierda para ver el detalle, o instale uno primero.',

  // ── Página de plugin (pages/plugins/PluginPage.tsx) ─────────────────────
  'plugins.page.uninstalled': 'El plugin se desinstaló y se ha vuelto al inicio.',
  'plugins.page.disabled': 'El plugin está deshabilitado y se ha vuelto al inicio.',
  'plugins.page.missing':
    'La entrada de página del plugin no existe o no está disponible; se ha vuelto al inicio.',
  'plugins.page.sessionExpired':
    'La sesión del navegador ha caducado; vuelva a obtener la dirección de entrada con token y ábrala de nuevo.',
  'plugins.page.httpFailed':
    'Los recursos de la página no están disponibles (HTTP {status}). Puede deshabilitar y volver a habilitar el plugin en la gestión de plugins, o actualizarlo.',
  'plugins.page.loadFailed':
    'Error al cargar los recursos de la página; compruebe si el host está en línea.',
  'plugins.page.reload': 'Recargar',
  'plugins.page.manage': 'Gestión de plugins',
  'plugins.page.loading': 'Cargando la página del plugin…',

  // ── Instalar plugin (pages/plugins/InstallPanel.tsx) ────────────────────
  'plugins.install.tooLarge': 'El paquete de plugin supera el límite de {max} MB',
  'plugins.install.sampleCopied': 'Ejemplo de plugin.json copiado',
  'plugins.install.copyFailed': 'Error al copiar; seleccione manualmente el texto del ejemplo',
  'plugins.install.title': 'Instalar plugin',
  'plugins.install.hint':
    'Suba el paquete zip seleccionado en el navegador; se habilitará automáticamente tras instalarse.',
  'plugins.install.pick': 'Seleccionar paquete zip',
  'plugins.install.selectedFile': '{name} ({size})',
  'plugins.install.notPicked': 'Aún no se ha seleccionado ningún archivo',
  'plugins.install.clear': 'Quitar',
  'plugins.install.installing': 'Instalando…',
  'plugins.install.uploadAndInstall': 'Subir e instalar',
  'plugins.install.structure': 'Estructura del paquete',
  'plugins.install.tree':
    'my-plugin.zip\n└─ com.example.myplugin/          # el nombre del directorio raíz es libre; se recomienda usar el id\n   ├─ plugin.json                 # obligatorio\n   ├─ lib/                        # ensamblado de entrada y sus dependencias\n   │  └─ Com.Example.MyPlugin.dll\n   ├─ web/                        # opcional: páginas incluidas (webPage debe estar aquí)\n   │  ├─ index.html\n   │  └─ app.js\n   └─ assets/                     # opcional: iconos y otros recursos',
  'plugins.install.requiredFields':
    'plugin.json debe incluir id, name, version y entryAssembly; uiContributions, skills y mcpServers son opcionales.',
  'plugins.install.dll':
    'Debe incluir un DLL y el tipo de entrada de entryAssembly debe implementar IPlugin; el plugin solo comparte los LuBan.* / Microsoft.* / System.* ya cargados por el host, no incluya esos ensamblados.',
  'plugins.install.webPageRule':
    'webPage debe apuntar a un archivo .html dentro de web/ de este paquete; las rutas relativas de la página funcionan, pero las extensiones fuera de la lista blanca (p. ej. .woff2, .json) pueden no cargarse.',
  'plugins.install.packageRule':
    'Ponga el manifiesto en la raíz del comprimido o deje un solo directorio de nivel superior; cada paquete no debe superar los 32 MB ni los 128 MB descomprimido.',
  'plugins.install.autoEnable':
    'Se habilita automáticamente tras la instalación; si ya existe el mismo id, use «Actualizar».',
  'plugins.install.minExample': 'Ejemplo mínimo de plugin.json',
  'plugins.install.manifest':
    '{\n  "id": "com.example.myplugin",\n  "name": "Plugin de ejemplo",\n  "version": "1.0.0",\n  "entryAssembly": "Com.Example.MyPlugin.dll",\n  "uiContributions": [\n    { "id": "main", "label": "Ejemplo", "icon": "plug", "webPage": "web/index.html" }\n  ]\n}',
  'plugins.install.copy': 'Copiar',
  'plugins.install.securityNotice':
    'La página del plugin se ejecuta en el mismo origen que el host, por lo que sus scripts pueden acceder al almacenamiento del navegador y al DOM de la página actual. Instale solo plugins de fuentes de confianza.',

  // ── Ajustes de notificaciones (pages/settings/NotifyTab.tsx) ────────────
  'notify.title': 'Tonos',
  'notify.preview': 'Escuchar',
  'notify.mutedHint':
    'Si la pestaña del navegador o el sistema están silenciados, no se oirá; es un ajuste del sistema.',
  'notify.kind.done.label': 'Tarea completada',
  'notify.kind.done.hint':
    'Suena al terminar correctamente un turno de conversación: tres notas agudas ascendentes; no suena si se detiene manualmente o si el turno falla.',
  'notify.kind.question.label': 'Requiere tu acción',
  'notify.kind.question.hint':
    'Suena cuando aparece una pregunta o una llamada a herramienta espera tu confirmación: dos notas iguales, corta-larga.',
  'notify.kind.error.label': 'Error en la conversación',
  'notify.kind.error.hint':
    'Suena si falla el turno, se corta la conexión o falla la reanudación: dos notas graves descendentes; no suena si falla una sola herramienta (el Agent suele reintentar automáticamente).',

  // ── Directorios raíz (pages/settings/RootsTab.tsx) ──────────────────────
  'roots.title': 'Directorios raíz',
  'roots.empty':
    'Aún no hay directorios raíz configurados; el servidor usará ~/.luban-agent como alternativa.',
  'roots.saved': 'Directorios raíz del espacio de trabajo guardados',
  'roots.duplicate': 'Ese directorio ya está en la lista',
  'roots.keepOne': 'Debe conservarse al menos un directorio raíz',
  'roots.removeConfirm':
    '¿Quitar el directorio raíz {root}?\nLos espacios de trabajo ya registrados en ese directorio seguirán funcionando, pero no se podrán crear nuevos proyectos en él.',
  'roots.allowedNote': 'Permite crear nuevos proyectos en este directorio',
  'roots.newRoot': 'Añadir directorio raíz',
  'roots.absolutePath': 'Ruta absoluta',
  'roots.absolutePathHint':
    'Debe indicarse una ruta absoluta de esta máquina; un archivo existente no puede usarse como directorio raíz',
  'roots.globalNote':
    'Configuración de la máquina compartida por todos los usuarios: los proyectos nuevos (directorio indicado y clonación de git) solo pueden crearse dentro de estos directorios raíz. Quitarlos de la lista no afecta a los espacios de trabajo ya registrados.',

  // ── Datos y uso (pages/settings/DataTab.tsx) ────────────────────────────
  'data.overview': 'Resumen de uso',
  'data.allTime': 'Todo el tiempo',
  'data.last7': 'Últimos 7 días',
  'data.last30': 'Últimos 30 días',
  'data.sessions': 'Conversaciones',
  'data.messages': 'Mensajes',
  'data.totalTokens': 'Token acumulados',
  'data.daysCovered': 'Estadísticas de {days} días',
  'data.avgDailyTokens': 'Token diarios medios',
  'data.cleanup': 'Limpieza de datos',
  'data.scope': 'Ámbito de limpieza',
  'data.allWorkspaces': 'Todos los espacios de trabajo',
  'data.scopeHint':
    'Detenga las conversaciones en curso antes de limpiar; «Todos los espacios de trabajo» borra las conversaciones de cada espacio (incluidos los de la base de conocimiento), pero no el contenido de la base de conocimiento',
  'data.clearing': 'Limpiando…',
  'data.clearButton': 'Borrar datos de conversaciones',
  'data.clearConfirmAll':
    '¿Eliminar las conversaciones y mensajes de todos los espacios de trabajo? Esta acción no se puede deshacer.',
  'data.clearConfirmWorkspace':
    '¿Eliminar todas las conversaciones y mensajes del espacio de trabajo «{name}»? Esta acción no se puede deshacer.',
  'data.cleared': 'Conversaciones borradas',
  'data.workspaceGone':
    'El espacio de trabajo seleccionado ya no existe; vuelva a elegir el ámbito de limpieza',

  // ── Servidores MCP (pages/settings/McpTab.tsx) ──────────────────────────
  'mcp.title': 'Servidores MCP',
  'mcp.empty': 'Aún no hay configuración MCP basada en archivos en este ámbito.',
  'mcp.nameRequired': 'El nombre del servidor no puede estar vacío',
  'mcp.nameHint': 'También se usa como nombre de archivo',
  'mcp.saved': 'Configuración MCP guardada',
  'mcp.deleted': 'Configuración MCP eliminada',
  'mcp.deleteConfirm':
    '¿Eliminar la configuración MCP «{name}»? Esta acción no se puede deshacer.',
  'mcp.transport': 'Transporte',
  'mcp.transportStdio': 'stdio (proceso local)',
  'mcp.transportHttp': 'http (Streamable HTTP)',
  'mcp.transportSse': 'sse (HTTP+SSE antiguo)',
  'mcp.startCommand': 'Comando de inicio',
  'mcp.serverUrl': 'Dirección del servidor',
  'mcp.startPlaceholder': 'p. ej. npx -y @modelcontextprotocol/server-filesystem',
  'mcp.urlPlaceholder': 'p. ej. http://127.0.0.1:3000/mcp',
  'mcp.args': 'Argumentos de inicio',
  'mcp.argsHint': 'Un argumento por línea',

  // ── Reglas (pages/settings/RuleTab.tsx) ─────────────────────────────────
  'rule.title': 'Reglas',
  'rule.empty': 'Aún no hay reglas basadas en archivos en este ámbito.',
  'rule.idRequired': 'El Id de la regla no puede estar vacío',
  'rule.saved': 'Regla guardada',
  'rule.deleted': 'Regla eliminada',
  'rule.deleteConfirm': '¿Eliminar la regla «{name}»? Esta acción no se puede deshacer.',
  'rule.actionTypePattern': 'Comodín del tipo de acción',
  'rule.targetPattern': 'Comodín del destino',
  'rule.action': 'Acción',
  'rule.actionAllow': 'allow (permitir)',
  'rule.actionDeny': 'deny (denegar)',
  'rule.priority': 'Prioridad',
  'rule.priorityHint': 'Cuanto mayor es el número, mayor es la prioridad',
  'rule.content': 'Texto de guía',
  'rule.contentPlaceholder':
    'Texto de guía que se añade al modelo cuando coincide la regla; puede dejarse vacío.',

  // ── Nivel de razonamiento (ModelTab / Chat) ─────────────────────────────
  'effort.none': 'Sin razonamiento',
  'effort.followModel': 'Seguir el modelo',
  'effort.low': 'Bajo',
  'effort.medium': 'Medio',
  'effort.high': 'Alto',
  'effort.extraHigh': 'Muy alto',

  // ── Página de modelos (pages/settings/ModelTab.tsx) ─────────────────────
  'model.title': 'Modelos',
  'model.empty': 'Aún no hay proveedores configurados; añada uno en la pestaña «Proveedores».',
  'model.selectProvider': 'Seleccione un proveedor.',
  'model.defaultModelHint':
    'Lo que se elige aquí es el «modelo predeterminado de nuevas conversaciones»; las conversaciones que ya tienen un modelo propio no se ven afectadas. El modelo de cada conversación se cambia en la parte inferior de la página de chat.',
  'model.noModels': 'Este proveedor no tiene modelos por ahora.',
  'model.custom': 'Personalizado',
  'model.capability': 'Capacidad',
  'model.defaultForNew': 'Predeterminado para nuevas',
  'model.setDefault': 'Establecer como predeterminado de nuevas conversaciones',
  'model.addCustom': 'Añadir modelo personalizado',
  'model.addCustomPlaceholder': 'Identificador del modelo, p. ej. qwen3.8-max',
  'model.profileTitle': 'Capacidades del modelo',
  'model.restoreDefault': 'Restaurar valores integrados',
  'model.effortsSection': 'Nivel de razonamiento',
  'model.effortsHint':
    'Tres estados excluyentes: «Desconocido» no se registra y el chat solo ofrece «Seguir el modelo»; «Sin razonamiento» oculta explícitamente el control de niveles; «Niveles específicos» se envía según lo marcado.',
  'model.modeUnknown': 'Desconocido (no registrar)',
  'model.modeNone': 'Sin razonamiento',
  'model.modeSome': 'Niveles específicos',
  'model.windowSection': 'Ventana de contexto (token)',
  'model.windowPlaceholder': 'Dejar vacío si se desconoce',
  'model.windowTiers': 'Niveles de presupuesto de contexto derivados de la ventana: {tiers}',
  'model.windowUnknownHint':
    'Si se desconoce la ventana, el chat no muestra el control de contexto.',
  'model.refreshed': 'Lista de modelos actualizada',
  'model.added': 'Modelo personalizado añadido',
  'model.renamePrompt': 'Renombrar el modelo «{name}» como:',
  'model.renamed': 'Modelo renombrado',
  'model.deleteConfirm': '¿Eliminar el modelo personalizado «{name}»?',
  'model.deleted': 'Modelo eliminado',
  'model.effortsNoneSelected':
    'Se eligió «Niveles específicos» pero no hay ninguno marcado; para indicar que no admite razonamiento elija «Sin razonamiento»',
  'model.windowInvalid':
    'La ventana de contexto debe ser un entero positivo (dejar vacío si se desconoce)',
  'model.restored': 'Valores integrados restaurados',
  'model.profileSaved': 'Perfil de capacidades guardado',

  // ── Página del espacio de trabajo (pages/settings/WorkspaceTab.tsx) ─────
  'workspace.title': 'Espacio de trabajo',
  'workspace.rootPath': 'Directorio raíz',
  'workspace.unauthorized':
    'El directorio aún no está autorizado; el Agent no puede leer ni escribir en este espacio de trabajo.',
  'workspace.authorized':
    'El directorio está autorizado; el Agent puede leer y escribir en este espacio de trabajo.',
  'workspace.configDir': 'Directorio de configuración',
  'workspace.notGenerated': '(aún no generado)',
  'workspace.configDirHint':
    'Las habilidades, las reglas y la configuración MCP se guardan en este directorio',
  'workspace.authorizing': 'Autorizando…',
  'workspace.authorize': 'Autorizar acceso al directorio',
  'workspace.generating': 'Generando…',
  'workspace.generateConfig': 'Generar directorio de configuración',
  'workspace.agentsInit': 'Inicialización de AGENTS.md',
  'workspace.existsChars': 'Ya existe · {count} caracteres',
  'workspace.notCreated': 'Aún no creado',
  'workspace.agentName': 'Nombre del Agent',
  'workspace.agentNamePlaceholder': 'Por ejemplo: asistente de pasarela de pago',
  'workspace.outputMode': 'Modo de salida',
  'workspace.full': 'Versión completa',
  'workspace.lite': 'Versión reducida',
  'workspace.scene': 'Caso de uso',
  'workspace.sceneHint': 'Obligatorio: qué problema debe resolver este espacio de trabajo',
  'workspace.capabilities': 'Lista de capacidades',
  'workspace.capabilitiesHint': 'Opcional: separar con comas o saltos de línea',
  'workspace.tools': 'Herramientas / API',
  'workspace.toolsHint': 'Opcional: separar con comas o saltos de línea',
  'workspace.description': 'Notas adicionales',
  'workspace.descriptionHint': 'Opcional: restricciones, límites y prohibiciones adicionales',
  'workspace.overwrite': 'Sobrescribir el AGENTS.md existente',
  'workspace.overwriteHint':
    'Si no se marca, se rechazará directamente la generación y el guardado cuando ya exista',
  'workspace.generateAndWrite': 'Generar y escribir',
  'workspace.saveEdits': 'Guardar cambios',
  'workspace.copyVerification': 'Copiar la indicación de verificación',
  'workspace.result': 'Resultado generado (editable antes de guardar)',
  'workspace.authorizedToast': 'Acceso autorizado a este directorio',
  'workspace.configReady': 'Directorio de configuración listo',
  'workspace.written': 'AGENTS.md escrito en la raíz del espacio de trabajo',
  'workspace.required': 'El nombre del Agent y el caso de uso son obligatorios',
  'workspace.verificationCopied': 'Indicación de verificación copiada',
  'workspace.clipboardDenied':
    'El navegador ha denegado el acceso al portapapeles; seleccione y copie manualmente',

  // ── Gestión de usuarios (pages/settings/UsersTab.tsx) ───────────────────
  'users.title': 'Usuarios',
  'users.empty': 'Aún no hay usuarios.',
  'users.selectEmpty': 'Seleccione un usuario a la izquierda para ver el detalle.',
  'users.userIdHint':
    'Los espacios de trabajo y las conversaciones pertenecen a este identificador; no cambia tras el registro',
  'users.lastLogin': 'Último inicio de sesión',
  'users.neverLoggedIn': 'Nunca ha iniciado sesión',
  'users.loginState': 'Estado de sesión',
  'users.lockHint':
    'Tras 5 contraseñas incorrectas seguidas, la cuenta se bloquea 30 minutos y se desbloquea automáticamente al vencer',
  'users.newPasswordHint': 'Solo se muestra esta vez; no se podrá recuperar al salir de esta página',
  'users.resetPassword': 'Restablecer contraseña',
  'users.resetSelf': 'No puede restablecer su propia contraseña; cámbiela en la página «Cuenta»',
  'users.resetBuiltIn': 'El administrador integrado solo puede cambiar su propia contraseña',
  'users.unlock': 'Desbloquear',
  'users.unlockTitle': 'Quitar el bloqueo para que el usuario pueda iniciar sesión de inmediato',
  'users.unlockCountTitle': 'Poner a cero el contador de fallos',
  'users.copyPassword': 'Copiar la nueva contraseña',
  'users.builtInNote':
    'El administrador integrado es la cuenta de nivel superior; solo él puede cambiar su contraseña en la página «Cuenta» y los administradores no pueden restablecerse mutuamente la contraseña.',
  'users.lockedUntil': 'Bloqueado hasta {time}',
  'users.canLoginFailed': 'Puede iniciar sesión ({count} fallos seguidos)',
  'users.normal': 'Normal',
  'users.unlocked': 'Desbloqueado',
  'users.noLock': 'Este usuario no tiene bloqueos activos',
  'users.resetConfirm':
    '¿Restablecer la contraseña de «{name}»? La sesión actual de ese usuario caducará de inmediato.',
  'users.resetOk': 'Se ha generado una nueva contraseña; entréguesela al usuario de inmediato',
  'users.deleteConfirm': 
    '¿Eliminar al usuario «{name}»? Se cerrará su sesión de inmediato y no podrá volver a iniciar sesión; también se eliminarán sus credenciales Git guardadas.',
  'users.deleted': 'Usuario eliminado',
  'users.deleteSelf': 'No puedes eliminar tu propia cuenta',
  'users.deleteBuiltIn': 'El administrador integrado no se puede eliminar',
  'users.adminSuffix': ' · Administrador',
  'users.builtinSuffix': ' · Integrado',

  // ── Invitaciones (pages/settings/InviteTab.tsx) ─────────────────────────
  'invite.title': 'Invitaciones',
  'invite.empty':
    'Aún no hay códigos de invitación. Pulse «Nuevo» para generar uno y enviarlo a quien deba registrarse.',
  'invite.used': 'Usado',
  'invite.expired': 'Caducado',
  'invite.unused': 'Sin usar',
  'invite.expiresAtSuffix': '{status} · caduca el {time}',
  'invite.created': 'Código de invitación {code} generado',
  'invite.revokeConfirm':
    '¿Anular el código de invitación {code}? Una vez anulado no podrá usarse para registrarse.',
  'invite.revoked': 'Código de invitación anulado',
  'invite.usedDeleteNote':
    'Este código ya lo usó {user}; si se elimina, no se podrá rastrear el origen del registro.',
  'invite.deleteNote': 'No se puede deshacer tras eliminarlo.',
  'invite.deleteConfirm': '¿Eliminar el código de invitación {code}? {note}',
  'invite.deleted': 'Código de invitación eliminado',
  'invite.generateTitle': 'Generar código de invitación',
  'invite.validDays': 'Días de validez',
  'invite.expireHint': 'Los códigos sin usar caducan automáticamente al vencer',
  'invite.generate': 'Generar código de invitación',
  'invite.code': 'Código de invitación',
  'invite.codeHint': 'De un solo uso: caduca en cuanto se completa el registro',
  'invite.expiresAt': 'Fecha de caducidad',
  'invite.creator': 'Creador',
  'invite.usedBy': 'Usado por',
  'invite.copyCode': 'Copiar código de invitación',
  'invite.revoke': 'Anular',

  // ── Proveedores (pages/settings/ProviderTab.tsx) ────────────────────────
  'provider.title': 'Proveedores',
  'provider.empty': 'Aún no hay proveedores configurados.',
  'provider.preset': 'Proveedor',
  'provider.presetHint':
    'Al elegir un proveedor integrado, el identificador y la URL base se fijan según el preajuste (algunos requieren indicar el endpoint); elija «Personalizado» para rellenarlos a mano',
  'provider.id': 'Identificador',
  'provider.idHintCustom':
    'En minúsculas; actúa como clave única, cambiarlo equivale a renombrar',
  'provider.idHintBuiltin':
    'El identificador de los proveedores integrados es fijo; para cambiarlo elija «Personalizado» en «Proveedores»',
  'provider.apiKeyCurrent': 'Actual: {mask}; dejar vacío para no cambiar',
  'provider.apiKeyNew': 'Obligatorio al crear',
  'provider.baseUrlPlaceholder': 'Dejar vacío para usar la dirección predeterminada',
  'provider.timeout': 'Tiempo de espera de red (s)',
  'provider.timeoutHint': 'Dejar vacío para usar el valor predeterminado',
  'provider.customModels': 'Modelos personalizados',
  'provider.customModelsHint': 'Uno por línea; al guardar se añaden o quitan por diferencia',
  'provider.endpointEditable': 'Este proveedor integrado requiere indicar la URL del endpoint',
  'provider.endpointFixed':
    'Dirección predeterminada integrada; para cambiarla elija «Personalizado» en «Proveedores»',
  'provider.required': 'El identificador del proveedor no puede estar vacío',
  'provider.exists': 'El identificador de proveedor «{name}» ya existe',
  'provider.existsEdit':
    'El identificador de proveedor «{name}» ya existe; selecciónelo en la lista de la izquierda para editarlo',
  'provider.apiKeyRequired': 'Al crear un proveedor, la API Key no puede estar vacía',
  'provider.saved': 'Proveedor guardado',
  'provider.deleted': 'Proveedor eliminado',
  'provider.deleteConfirm':
    '¿Eliminar el proveedor «{name}»? Si el modelo actual pertenece a él, la selección de modelo también se borrará; las conversaciones que tenían un modelo de este proveedor volverán al modelo predeterminado.',

  // ── Habilidades (pages/settings/SkillTab.tsx) ───────────────────────────
  'skill.title': 'Habilidades',
  'skill.empty': 'Aún no hay habilidades basadas en archivos en este ámbito.',
  'skill.importZip': 'Importar ZIP',
  'skill.nameHintEdit': 'Directorio {key} (guardar no lo mueve)',
  'skill.nameHintNew': 'Al crear, el nombre del directorio se genera a partir del nombre',
  'skill.category': 'Categoría',
  'skill.categoryHint': 'La lista de la izquierda se agrupa por esto',
  'skill.descriptionHint':
    'Obligatorio; el modelo lo usa para decidir si emplea esta habilidad',
  'skill.triggers': 'Palabras de activación',
  'skill.triggersHint': 'Separadas por comas',
  'skill.body': 'Cuerpo',
  'skill.bodyPlaceholder': 'Cuerpo en Markdown; indique los casos de uso y los pasos.',
  'skill.uncategorized': 'Sin categoría',
  'skill.nameRequired': 'El name de la habilidad no puede estar vacío',
  'skill.descriptionRequired': 'La description de la habilidad no puede estar vacía',
  'skill.saved': 'Habilidad guardada',
  'skill.deleted': 'Habilidad eliminada',
  'skill.deleteConfirm':
    '¿Eliminar la habilidad «{name}» (directorio {key})? Esta acción no se puede deshacer.',
  'skill.imported': '{count} habilidades importadas',
  'skill.importSkipped': '{count} elementos omitidos',
  'skill.importIgnored':
    '{count} archivos que no son SKILL.md no se importaron (solo se admiten habilidades de un archivo)',

  // ── Flujo de conversación (stores/chat.ts) ──────────────────────────────
  'chat.ctxCompactedHistory': '📦 Contexto compactado (resumen del historial)',
  'chat.ctxCompacted': '📦 Contexto compactado',
  'chat.toolFailed': 'Error al ejecutar la herramienta',
  'chat.toolDeniedNotice': 'Llamada a herramienta rechazada: {name}',
  'chat.toolCallFallback': 'Llamada a herramienta',
  'chat.attachmentSkipped': 'Adjunto omitido: {source} — {reason}',
  'chat.requestFailed': 'Error en la solicitud',
  'chat.retrying':
    '⟳ {label}: reintentando automáticamente (intento {attempt} de {maxRetries})',
  'chat.chatFailed': 'Error en la conversación',
  'chat.resumeFailed': 'Error al reanudar el flujo; vuelva a abrir esta conversación',
  'chat.connectionFailed': 'Error de conexión de la conversación',
  'chat.attachmentsUploading': 'Los adjuntos aún se están subiendo; espere',
  'chat.connectionInterrupted': 'Conexión interrumpida; este turno sigue ejecutándose en segundo plano',
  'chat.reconnecting': 'Conexión interrumpida; reconectando',
  'chat.previousRoundNotFinished': 'El turno anterior aún no ha terminado; este mensaje no se envió',
  'chat.stopRequested': 'Se ha solicitado detener este turno',
  'chat.toolDenied': 'Se ha rechazado esta llamada a herramienta',
  'chat.confirmTimeout': 'La confirmación ha caducado; se trata como rechazo',
  'chat.questionTimeout': 'La pregunta ha caducado; se ha omitido la respuesta',
  'chat.maxAttachments': 'Máximo {count} adjuntos por turno',
  'chat.maxAttachmentsIgnored': 'Máximo {count} adjuntos por turno; los sobrantes se ignoraron',
  'chat.unknownFileType': 'No se puede identificar el tipo de archivo: {name}',
  'chat.unsupportedAttachment': 'Tipo de adjunto no admitido: {ext}',
  'chat.undecodable': 'No se puede decodificar {ext}; conviértalo primero a PNG o JPEG',
  'chat.textTooLarge': 'Los adjuntos de texto no pueden superar {size}',
  'chat.imageTooLarge': 'Las imágenes no pueden superar {size}',
  'chat.uploadFailed': 'Error al subir',
  'chat.compressedImages': '{count} imágenes comprimidas: {from} → {to}',

  // ── Página de la base de conocimiento (pages/LlmWiki.tsx) ───────────────
  'wiki.sectionTitle': 'Base de conocimiento',
  'wiki.new': 'Nueva base de conocimiento',
  'wiki.renameTitle': 'Renombrar base de conocimiento',
  'wiki.deleteTitle': 'Eliminar base de conocimiento',
  'wiki.emptySide': 'Aún no hay bases de conocimiento; cree una arriba a la derecha.',
  'wiki.resizeLabel': 'Ajustar el ancho de la barra de la base de conocimiento',
  'wiki.emptyTitle': 'Aún no hay bases de conocimiento',
  'wiki.emptyHint':
    'La base de conocimiento divide en fragmentos e indexa los materiales de un directorio para que el chat los recupere y los cite.',
  'wiki.authorizing': 'Autorizando…',
  'wiki.authorize': 'Autorizar acceso al directorio',
  'wiki.pageCount': 'páginas wiki',
  'wiki.indexedFiles': 'Archivos indexados',
  'wiki.indexedChunks': 'Fragmentos indexados',
  'wiki.lastRebuild': 'Última reconstrucción: {summary}',
  'wiki.globPlaceholder': 'Patrón de archivos a indexar (opcional, p. ej. **/*.md)',
  'wiki.indexing': 'Indexando…',
  'wiki.index': 'Indexar',
  'wiki.rebuilding': 'Reconstruyendo…',
  'wiki.rebuild': 'Reconstruir',
  'wiki.linting': 'Validando…',
  'wiki.clearIndex': 'Borrar índice',
  'wiki.searchPlaceholder': 'Buscar en la base de conocimiento',
  'wiki.search': 'Buscar',
  'wiki.hits': 'Resultados ({count})',
  'wiki.lintReport': 'Informe de Lint · {pages} páginas analizadas · {findings} hallazgos',
  'wiki.lintClean': 'No se han encontrado problemas',
  'wiki.indexedFilesHeading': 'Archivos indexados ({count})',
  'wiki.noIndex': 'Aún no hay índice; pulse «Indexar» arriba para empezar.',
  'wiki.unknownLanguage': 'Idioma desconocido',
  'wiki.chunks': '{count} fragmentos',
  'wiki.needIndex':
    'Complete la indexación (con archivos y fragmentos indexados) para poder conversar',
  'wiki.chatTitleReady': 'Conversar con la base de conocimiento (recuperación aumentada automática)',
  'wiki.chatTitleNotReady': 'Complete la indexación para poder conversar',
  'wiki.opening': 'Abriendo…',
  'wiki.chat': 'Conversar',
  'wiki.createSubtitle':
    'La base de conocimiento corresponde a un directorio de esta máquina e indexa sus materiales',
  'wiki.creating': 'Creando…',
  'wiki.create': 'Crear',
  'wiki.nameOptional': 'Nombre (opcional)',
  'wiki.defaultNamePlaceholder': 'Por defecto se usa el nombre del directorio',
  'wiki.renameSubtitle':
    'Solo cambia el nombre visible; el directorio en disco y el índice no se ven afectados',
  'wiki.name': 'Nombre',
  'wiki.deleteMessage':
    '¿Eliminar «{name}»? También se borrarán las conversaciones, el índice y la configuración .luban-agent de esta base de conocimiento; el resto de archivos del directorio en disco no se verán afectados.',
  'wiki.clearMessage':
    '¿Borrar los archivos y el índice de fragmentos de esta base de conocimiento? Los materiales en disco no se verán afectados y podrá volver a indexarlos después.',
  'wiki.created': 'Base de conocimiento creada',
  'wiki.deleted': 'Base de conocimiento eliminada',
  'wiki.renamed': 'Base de conocimiento renombrada',
  'wiki.authorized': 'Acceso autorizado a este directorio',
  'wiki.cleared': 'Índice borrado',
  'wiki.chatSessionTitle': 'Conversación de la base de conocimiento',
  'wiki.op.index': 'Indexar',
  'wiki.op.rebuild': 'Reconstruir',
  'wiki.report': '{prefix}: {summary}',
  'wiki.doneWithErrors': '{prefix} completado, pero {count} elementos han fallado',
  'wiki.done': '{prefix} completado',
  'wiki.lintDone': 'lint completado: {count} hallazgos',

  // ── Página de chat (pages/Chat.tsx) ─────────────────────────────────────
  'chat.untitledSession': 'Conversación sin título',
  'chat.tokenTotalOnly': 'Esta conversación ha consumido {total} token en total',
  'chat.tokenWindowUsage':
    'Ventana actual: {used} / {window} token ({percent}%{estimated}); total de la conversación: {total} token',
  'chat.windowEstimated': ', ventana estimada según el nivel de presupuesto de contexto',
  'chat.skillsLoadFailed': 'Error al cargar la lista de habilidades',
  'chat.effortUnsupported':
    'El modelo actual no admite este nivel de razonamiento; se ha cambiado a seguir el modelo',
  'chat.commandBusy': 'Este turno está en curso; el comando no está disponible por ahora',
  'chat.permissionModeToast': 'Modo de permisos: {mode}',
  'chat.sessionStats': '{tokens} Token acumulados · {count} mensajes',
  'chat.unknownCommand': 'El comando {name} aún no es compatible',
  'chat.clearWarningCount':
    'Los {count} mensajes de esta conversación se eliminarán de forma permanente y no se podrán recuperar.',
  'chat.clearWarning':
    'Los mensajes de esta conversación se eliminarán de forma permanente y no se podrán recuperar.',
  'chat.clearWarningStreaming': '{head}el turno que se está generando se interrumpirá primero.',
  'chat.commandIrreversible':
    'Este comando no se puede deshacer una vez ejecutado. ¿Desea continuar?',
  'chat.cleared': 'Mensajes borrados',
  'chat.projectNoSession': 'Este proyecto aún no tiene conversaciones',
  'chat.resizeProjectBar': 'Ajustar el ancho de la barra de proyectos',
  'chat.backToList': 'Volver a la lista',
  'chat.unknownProject': 'Proyecto desconocido',
  'chat.viewWorkspace': 'Ver espacio de trabajo',
  'chat.more': 'Más',
  'chat.loadingMessages': 'Cargando mensajes…',
  'chat.startTitle': 'Empezar a conversar',
  'chat.startHint':
    'Escriba una pregunta o pulse «+» abajo a la izquierda para añadir imágenes y archivos.',
  'chat.plan': 'Plan',
  'chat.subagentProgress': 'Progreso del subagente',
  'chat.generating': 'Generando',
  'chat.toTop': 'Ir arriba',
  'chat.toBottom': 'Ir abajo',
  'chat.viewLarge': 'Pulse para ver la imagen a tamaño completo',
  'chat.uploading': 'Subiendo…',
  'chat.optimizing': 'Procesando…',
  'chat.composerPlaceholder':
    'Enviar un mensaje a Luban Agent (Enter para enviar, Shift+Enter para nueva línea)',
  'chat.addFilesTitle': 'Añadir imágenes y archivos / comandos',
  'chat.switchModel': 'Cambiar el modelo de esta conversación (no afecta a otras)',
  'chat.switchPermission': 'Cambiar el modo de permisos',
  'chat.effortTitle': 'Intensidad de razonamiento (esta conversación)',
  'chat.ctxFixedTitle':
    'Presupuesto de contexto (se compacta automáticamente al superarlo): ventana {window}; una ventana fija no compacta antes',
  'chat.ctxLabel': 'Contexto {tier}',
  'chat.ctxTitle':
    'Presupuesto de contexto (se compacta automáticamente al superarlo): cuanto menor es el nivel, antes se compacta',
  'chat.optimizeTitle': 'Reescribir el texto introducido como una descripción más clara de la necesidad',
  'chat.sendQueued': 'Añadir a la cola de envío',
  'chat.stopRound': 'Detener este turno',
  'chat.send': 'Enviar',
  'chat.builtinSkill': 'Integrada',
  'chat.attachmentsHint':
    'Máximo {count} adjuntos · arrastrar y soltar y pegar · Enter para enviar / Shift+Enter para nueva línea',
  'chat.resizeWorkspace': 'Ajustar el ancho del espacio de trabajo',
  'chat.screenshotTitle':
    'Seleccione una zona de la pantalla y anótela; al confirmar se añadirá como adjunto',
  'chat.screenshotUnsupported':
    'La dirección de acceso actual no es un contexto seguro y el navegador no permite capturar la pantalla; abra la página con https o localhost',
  'chat.screenshot': 'Captura',
  'chat.needsHttps': 'Requiere https',
  'chat.imagesAndFiles': 'Imágenes y archivos',
  'chat.commands': 'Comandos',
  'chat.ctxMenuTitle': 'Presupuesto de contexto',
  'chat.windowLabel': 'Ventana',
  'chat.ctxNote':
    'Se compacta automáticamente al superarlo · cuanto menor es el nivel, antes se compacta',
  'chat.ctxTierTitle':
    'Cuanto menor es el nivel, antes se activa la compactación: ahorra token, pero descarta el historial antes',
  'chat.tierDefault': 'Predeterminado',
  'chat.tierEarly': 'Compactación anticipada',
  'chat.noProviders': 'Aún no hay proveedores',
  'chat.loadingModels': 'Obteniendo la lista de modelos…',
  'chat.clearMessages': 'Borrar mensajes',
  'chat.deleteSession': 'Eliminar conversación',
  'chat.renameSessionTitle': 'Renombrar conversación',
  'chat.titleLabel': 'Título',
  'chat.deleteSessionConfirm':
    '¿Eliminar «{name}»? También se eliminarán los mensajes de esta conversación.',
  'chat.runCommandTitle': '¿Ejecutar {name}?',
  'chat.continue': 'Continuar',
  'chat.queueNote': 'En cola · se enviará automáticamente al terminar este turno',
  'chat.sending': 'Enviando…',
  'chat.notSent': 'No enviado',
  'chat.history': 'Historial',
  'chat.replyDuration': 'Duración de la respuesta de este turno',
  'chat.tokenUsage': 'Uso de token de este turno',
  'chat.attachment': 'Adjunto',
  'chat.viewAttachment': 'Ver {name}',
  'chat.viewAttachmentPath': 'Ver {name} ({path})',
  'chat.thinking': 'Proceso de razonamiento',
  'chat.thinkingDuration': 'Duración del razonamiento',
  'chat.toolCalls': 'Llamadas a herramientas',
  'chat.toolCount': '{count}',
  'chat.toolDuration': 'Duración de las llamadas a herramientas',
  'chat.statusRunning': 'En ejecución',
  'chat.statusDone': 'Completado',
  'chat.statusFailed': 'Error',
  'chat.statusDenied': 'Rechazado',
  'chat.arguments': 'Argumentos',
  'chat.noArguments': '(sin argumentos)',
  'chat.resultSummary': 'Resumen del resultado',
  'chat.historyReplayNote':
    'La reproducción del historial solo conserva los argumentos de la llamada, no los resultados de ejecución de la herramienta.',
  'chat.confirmToolTitle': '¿Permitir la ejecución de {name}?',
  'chat.confirmToolSub': 'La llamada a la herramienta requiere tu confirmación',
  'chat.origin': 'Origen: nodo de orquestación {name}{role}{desc}',
  'chat.unnamed': 'Sin nombre',
  'chat.roleSuffix': ' (rol {role})',
  'chat.descSuffix': ': {desc}',
  'chat.rejectIn': 'Se rechazará en {seconds} s',
  'chat.deny': 'Rechazar',
  'chat.allowSession': 'Permitir todo en esta conversación',
  'chat.allowAll': 'Permitir todo en este turno',
  'chat.allow': 'Permitir',
  'chat.skipIn': 'Se omitirá automáticamente en {seconds} s',
  'chat.unansweredOne': 'Esta pregunta no se respondió',
  'chat.unansweredMany': 'Preguntas {list} sin responder',
  'chat.questionListSeparator': ', ',
  'chat.agentQuestions': 'El Agent tiene {count} preguntas',
  'chat.agentConfirm': 'El Agent quiere que confirmes',
  'chat.answerHint': 'El Agent continuará cuando respondas',
  'chat.questionNo': 'Pregunta {index}',
  'chat.questionNoAnswered': 'Pregunta {index} (respondida)',
  'chat.questionNoUnanswered': 'Pregunta {index} (sin responder)',
  'chat.questionCounter': 'Pregunta {current} de {total}',
  'chat.multiSelect': 'Selección múltiple',
  'chat.otherAnswer': 'Otra respuesta (personalizada)',
  'chat.customAnswerPlaceholder': 'Escriba una respuesta personalizada',
  'chat.clearOtherTitle': 'Borrar la respuesta personalizada y contraer',
  'chat.clear': 'Borrar',
  'chat.skip': 'Omitir respuesta',
  'chat.previous': 'Anterior',
  'chat.submitting': 'Enviando…',
  'chat.submit': 'Enviar',
  'chat.next': 'Siguiente',
  'chat.helpTitle': 'Comandos disponibles en el navegador',
  'chat.gotIt': 'Entendido',

  // ── Formato de fecha y números (lib/format.ts) ──────────────────────────
  'format.yesterdayAt': 'Ayer a las {time}',
  'format.dayBeforeYesterdayAt': 'Anteayer a las {time}',
  'format.justNow': 'Ahora mismo',
  'format.duration.lessThanSecond': '<1 s',
  'format.duration.partsSeparator': ' ',

  // ── Arranque y pantalla de error (App.tsx) ──────────────────────────────
  'app.checkingAuth': 'Verificando el estado de sesión…',
  'app.backendUnavailable': 'No se puede conectar con el backend',
  'app.bootFailed': 'Error de arranque',
  'app.connectingTitle': 'Conectando con LuBan Agent',
  'app.connectingDetail': 'Leyendo el espacio de trabajo, los proveedores y la configuración de modelos…',
  'app.retry': 'Reintentar',
  'app.help': 'Ayuda',
  'app.about': 'Acerca de',

  // ── Inicio de sesión / registro (LoginPage.tsx) ────────────────────────
  'login.subtitle': 'Inicie sesión para seguir usando sus espacios de trabajo y conversaciones',
  'login.registerSubtitle': 'Cree una cuenta con un código de invitación',
  'login.uninitialized':
    'La cuenta de administrador aún no se ha inicializado; consulte la contraseña inicial en la salida de la terminal del host.',
  'login.tabLogin': 'Iniciar sesión',
  'login.tabRegister': 'Registrarse',
  'login.userName': 'Nombre de usuario',
  'login.userNamePlaceholder': 'Nombre de inicio de sesión',
  'login.password': 'Contraseña',
  'login.passwordPlaceholder': 'Contraseña',
  'login.passwordNewPlaceholder': 'Al menos 8 caracteres',
  'login.displayName': 'Nombre visible (opcional)',
  'login.displayNamePlaceholder': 'Por defecto, igual que el nombre de usuario',
  'login.inviteCode': 'Código de invitación',
  'login.inviteCodePlaceholder': 'Generado por el administrador',
  'login.submitting': 'Espere…',
  'login.submit': 'Iniciar sesión',
  'login.submitRegister': 'Registrarse e iniciar sesión',
  'login.errorMissing': 'Introduzca el nombre de usuario y la contraseña',
  'login.errorPasswordShort': 'La contraseña debe tener al menos 8 caracteres',
  'login.errorInviteRequired': 'Introduzca el código de invitación',

  // ── Menú de idioma (i18n) ───────────────────────────────────────────────
  'language.button': 'Idioma',
  'language.menuTitle': 'Idioma de la interfaz',
  'language.saveFailed':
    'Error al guardar la preferencia de idioma; solo se ha aplicado en este equipo',

  // ── Menú de tema (TopBar.tsx) ───────────────────────────────────────────
  'theme.title': 'Tema',
  'theme.follow': 'Seguir el sistema',
  'theme.dark': 'Oscuro',
  'theme.light': 'Claro',

  // ── Barra superior (TopBar.tsx) ─────────────────────────────────────────
  'topbar.home': 'Inicio',
  'topbar.search': 'Buscar conversaciones',
  'topbar.newSession': 'Nueva conversación',
  'topbar.unnamedProject': 'Proyecto sin título',
  'topbar.unnamedSession': 'Conversación sin título',
  'topbar.running': 'Turno en curso',
  'topbar.waitingConfirm': 'Esperando confirmación de herramienta',
  'topbar.closeTab': 'Cerrar pestaña',
  'topbar.account': 'Cuenta',
  'topbar.changePassword': 'Cambiar contraseña',
  'topbar.logout': 'Cerrar sesión',
  'topbar.adminSuffix': ' · Administrador',

  // ── Ajustes de cuenta (settings/AccountTab.tsx) ─────────────────────────
  'account.title': 'Mi cuenta',
  'account.userName': 'Nombre de inicio de sesión',
  'account.displayName': 'Nombre visible',
  'account.displayNameHint': 'Si se deja vacío, se usa el nombre de inicio de sesión',
  'account.userId': 'Identificador de usuario',
  'account.userIdHint':
    'También es el valor de pertenencia del espacio de trabajo; no cambia tras el registro',
  'account.role': 'Rol',
  'account.roleAdmin': 'Administrador',
  'account.roleUser': 'Usuario estándar',
  'account.changePassword': 'Cambiar contraseña',
  'account.currentPassword': 'Contraseña actual',
  'account.currentPasswordHint': 'Rellénela solo si necesita cambiarla',
  'account.newPassword': 'Nueva contraseña',
  'account.newPasswordHint': 'Al menos 8 caracteres',
  'account.confirmPassword': 'Confirmar la nueva contraseña',
  'account.noChanges': 'No hay cambios que guardar',
  'account.errorCurrentRequired': 'Introduzca la contraseña actual',
  'account.errorNewShort': 'La nueva contraseña debe tener al menos 8 caracteres',
  'account.errorMismatch': 'Las dos nuevas contraseñas no coinciden',
  'account.updatedBoth':
    'Nombre visible y contraseña actualizados; las sesiones de otros dispositivos han caducado',
  'account.updatedName': 'Nombre visible actualizado',
  'account.updatedPassword':
    'Contraseña actualizada; las sesiones de otros dispositivos han caducado',
  'account.language': 'Idioma de la interfaz',
  'account.languageHint': 'El cambio se aplica al instante y se sincroniza en todos los dispositivos',

  // ── Acciones comunes adicionales ────────────────────────────────────────
  'common.gotIt': 'Entendido',
  'common.more': 'Más',
  'common.name': 'Nombre',
  'common.nameOptional': 'Nombre (opcional)',
  'common.create': 'Crear',
  'common.creating': 'Creando…',
  'common.refresh': 'Actualizar',
  'common.remove': 'Quitar',
  'common.error': 'Error',
  'common.new': 'Nuevo',
  'common.description': 'Descripción',
  'common.enabled': 'Habilitado',
  'common.copied': 'Copiado',
  'common.copyFailed': 'Error al copiar; seleccione y copie manualmente',
  'common.fileReadFailed': 'Error al leer el archivo',
  'common.listSeparator': ', ',
  'common.rootPath': 'Ruta raíz (ruta absoluta)',

  // ── Editor del mensaje (components/ComposerEditor.tsx) ──────────────────
  'composer.skillPill': 'Habilidad: @{name}',

  // ── Acciones de archivo (components/FilePreviewActions.tsx) ─────────────
  'fileActions.copyFailed': 'Error al copiar: esta página no permite escribir en el portapapeles',
  'fileActions.copiedContent': 'Contenido del archivo copiado',
  'fileActions.copiedPath': 'Ruta del archivo copiada',
  'fileActions.backToPreview': 'Volver a la vista previa',
  'fileActions.viewSource': 'Ver código fuente',
  'fileActions.copyContent': 'Copiar el contenido del archivo',
  'fileActions.copyPath': 'Copiar la ruta del archivo',

  // ── Vista previa de archivos (components/FilePreviewDialog.tsx) ─────────
  'filePreview.failed': 'Error en la vista previa; inténtelo de nuevo más tarde.',
  'filePreview.reading': 'Leyendo…',
  'filePreview.fitWindow': 'Pulse para volver a ajustar a la ventana',
  'filePreview.actualSize': 'Pulse para ver al tamaño original',
  'filePreview.videoUnsupported':
    'El navegador no puede reproducir este vídeo (contenedor o códec no admitidos); descárguelo y reprodúzcalo localmente.',
  'filePreview.audioUnsupported':
    'El navegador no puede reproducir este audio; descárguelo y reprodúzcalo localmente.',

  // ── Ayuda y acerca de (components/InfoDialogs.tsx) ──────────────────────
  'info.readmeFailed': 'Error al leer el README',
  'info.readmeTitle': 'README de LuBan Agent (sincronizado con la compilación del host)',
  'info.readmeLoading': 'Leyendo el README…',
  'info.qrTitle': 'Abra esta interfaz en el móvil escaneando el código',
  'info.qrHint':
    'Con el móvil y el ordenador en la misma red local, escanee el código para abrir esta interfaz en el móvil',
  'info.description':
    'Solución de AI Agent basada en Microsoft Agent Framework: un único núcleo de ejecución unifica la TUI de línea de comandos, el cliente de escritorio multiplataforma y el servicio local de Agent.',
  'info.version': 'Versión',
  'info.versionUnknown': 'Desconocida (no se pudo leer la versión del host)',
  'info.repo': 'Repositorio',
  'info.author': 'Autor',
  'info.license': 'Licencia',
  'info.ok': 'Entendido',
  'info.helpTitle': 'Qué hacer si no se puede conectar con el host',
  'info.help.tokenPrefix': 'La entrada del navegador del host incluye un token de sesión (',
  'info.help.tokenBody':
    'parámetro); al abrirla se convierte en una Cookie y se llega a la página de inicio de sesión. La sesión dura 15 minutos por defecto y se renueva automáticamente si la página permanece abierta; al reiniciar el host caduca el enlace corto de entrada (el inicio de sesión del navegador sigue siendo válido mientras no caduque), y si hay un límite de sesiones configurado habrá que iniciar sesión de nuevo al llegar el momento.',
  'info.help.runInTerminal': 'En la terminal de la máquina host, ejecute',
  'info.help.rerun':
    ', que volverá a imprimir la «entrada del navegador»; cópiela entera y ábrala.',
  'info.help.restartPrefix':
    'Si no encuentra la terminal o el host ha terminado: reinicie',
  'info.help.restartSuffix':
    ' y busque de nuevo la entrada del navegador en el registro de arranque.',
  'info.help.forgotPassword':
    'Si olvidó la contraseña: pida al administrador que la restablezca en «Ajustes → Usuarios» (el administrador integrado solo puede cambiarla él mismo en «Ajustes → Cuenta»).',
  'info.help.stopPrefix': 'Para detener la instancia permanente:',
  'info.help.portPrefix': '; si el puerto está ocupado, use',
  'info.help.portSuffix': ' para cambiarlo.',
  'info.help.security':
    'Mientras la sesión sea válida, quien tenga la dirección de entrada y la cuenta puede leer y escribir el espacio de trabajo y ejecutar comandos; no la reenvíe a otras personas.',

  // ── Bloques de código Markdown (components/Markdown.tsx) ────────────────
  'markdown.copyCode': 'Copiar',
  'markdown.copied': 'Copiado',
  'markdown.copyFailed': 'Error al copiar',
  'markdown.previewInDialog':
    'Vista previa en un diálogo (Ctrl+clic para abrir en una pestaña nueva)',

  // ── Barra lateral de proyectos (components/ProjectSidebar.tsx) ──────────
  'sidebar.filterAgain': '· pulse otra vez para quitar el filtro',
  'sidebar.expand': 'Expandir la barra lateral',
  'sidebar.collapse': 'Contraer la barra lateral',
  'sidebar.projects': 'Proyectos',
  'sidebar.showAllProjects': 'Mostrar todos los proyectos',
  'sidebar.newProject': 'Nuevo proyecto',
  'sidebar.gitRepo': 'Repositorio Git',
  'sidebar.projectActions': 'Acciones del proyecto',
  'sidebar.noProjects': 'Aún no hay proyectos; cree uno arriba a la derecha.',
  'sidebar.wiki': 'Base de conocimiento',
  'sidebar.plugins': 'Plugins',
  'sidebar.settings': 'Ajustes',
  'sidebar.renameProject': 'Renombrar proyecto',
  'sidebar.renameHint': 'Solo cambia el nombre visible; el directorio en disco no cambia',
  'sidebar.deleteProject': 'Eliminar proyecto',
  'sidebar.deleteSessionsNote':
    'También se eliminarán las {count} conversaciones de este proyecto,',
  'sidebar.deleteConfirm':
    '¿Eliminar el proyecto «{name}»? {cascade}. También se borrará el directorio de configuración .luban-agent; el resto de archivos del directorio en disco no se verán afectados.',
  'sidebar.targetExists': 'El directorio de destino ya existe',
  'sidebar.createFailed': 'Error al crear',
  'sidebar.newProjectHint':
    'Un proyecto corresponde a un directorio de espacio de trabajo de esta máquina',
  'sidebar.source': 'Origen del proyecto',
  'sidebar.sourceDirectory': 'Directorio indicado',
  'sidebar.sourceGit': 'Repositorio git indicado',
  'sidebar.nameFromRepo':
    'Por defecto se usa el nombre del repositorio, también como nombre del directorio de clonación',
  'sidebar.nameFromDir': 'Por defecto se usa el nombre del directorio',
  'sidebar.rootHint':
    'Solo se puede crear dentro de los directorios raíz configurados por el administrador (por defecto ~/.luban-agent); si no está en la lista blanca se mostrarán los directorios permitidos.',
  'sidebar.repoUrl': 'Dirección del repositorio',
  'sidebar.cloneHint':
    'Se clona en el subdirectorio bajo la raíz seleccionada; si el directorio ya existe se pedirá confirmación primero y no se sobrescribirá.',
  'sidebar.reuseWorkspace': 'Compartir este espacio de trabajo',
  'sidebar.reuseDirectory': 'Reutilizar un directorio existente',

  // ── Sesión caducada (components/SessionExpired.tsx) ─────────────────────
  'sessionExpired.title': 'El token ha caducado',
  'sessionExpired.body':
    'El token del navegador se renueva periódicamente (15 minutos por defecto) y las páginas que permanecen abiertas se renuevan automáticamente; solo hay que volver a obtener la dirección de entrada si el host se reinicia (o si hay un límite de sesiones configurado y se alcanza el momento).',
  'sessionExpired.howtoPrefix':
    'Copie la dirección más reciente de «entrada del navegador» desde la terminal donde se inició el host (con',
  'sessionExpired.howtoMid': '), o ejecute en la máquina host',
  'sessionExpired.howtoSuffix': 'para imprimirla de nuevo.',
  'sessionExpired.reload': 'Recargar',

  // ── Arrastre lateral (components/SideResizer.tsx) ───────────────────────
  'resizer.sidebarWidth': 'Ajustar el ancho de la barra lateral',
  'resizer.dragHint': 'Arrastre para ajustar el ancho; doble clic para restaurar el valor predeterminado',

  // ── Capturas de pantalla (components/screenshot/*) ──────────────────────
  'screenshot.captureFailed': 'No se pudo obtener la imagen de la pantalla',
  'screenshot.noCanvas': 'Este navegador no admite Canvas 2D',
  'screenshot.insecure':
    'La dirección de acceso actual no permite capturas de pantalla; abra la página con https o localhost',
  'screenshot.captureError': 'Error al capturar la pantalla',
  'screenshot.fileName': 'captura-{time}.png',
  'screenshot.composeFailed': 'Error al componer la captura',
  'screenshot.downloaded': 'Descargado',
  'screenshot.textPlaceholder': 'Introduzca texto',
  'screenshot.tool.rect': 'Rectángulo',
  'screenshot.tool.ellipse': 'Elipse',
  'screenshot.tool.arrow': 'Flecha',
  'screenshot.tool.pen': 'Lápiz',
  'screenshot.tool.mosaic': 'Mosaico',
  'screenshot.tool.text': 'Texto',
  'screenshot.tool.arrowThin': 'Flecha de cola fina',
  'screenshot.tool.arrowWedge': 'Flecha de cola gruesa',
  'screenshot.tool.arrowDouble': 'Flecha de doble sentido',
  'screenshot.fontSize': 'Tamaño de fuente',
  'screenshot.blockSize': 'Tamaño de bloque',
  'screenshot.size': 'Tamaño',
  'screenshot.undo': 'Deshacer (Ctrl+Z)',
  'screenshot.cancel': 'Cancelar (Esc)',
  'screenshot.done': 'Hecho (Enter)',
  'screenshot.style': 'Estilo',
  'screenshot.inputDirectly': '{label}, se puede escribir directamente',
  'screenshot.fontSizeValue': 'Tamaño de fuente {size}px',
  'screenshot.color': 'Color',
  'screenshot.colorValue': 'Color {color}',
  'screenshot.stroke': 'Borde',
  'screenshot.strokeOff': 'Desactivar borde',
  'screenshot.strokeOn': 'Activar borde',
  'screenshot.strokeWidthHint':
    'Grosor del borde, desde 0.1px; se puede escribir directamente',
  'screenshot.strokeColorValue': 'Color del borde {color}',
  'screenshot.fill': 'Relleno',
  'screenshot.fillOff': 'Desactivar relleno',
  'screenshot.fillOn': 'Activar relleno',
  'screenshot.fillColorValue': 'Color de relleno {color}',

  // ── 新建项目向导（components/NewProjectWizard.tsx） ─────────────────────
  'wizard.stepSource': 'Origen',
  'wizard.stepLocation': 'Ubicación y credenciales',
  'wizard.stepConfirm': 'Confirmar',
  'wizard.back': 'Atrás',
  'wizard.next': 'Siguiente',
  'wizard.verifying': 'Verificando…',
  'wizard.verify': 'Verificar',
  'wizard.sourceDirectoryHint': 'Elige un directorio que ya exista en el disco o crea un subdirectorio nuevo',
  'wizard.sourceGitHint': 'Clona un repositorio HTTPS en un subdirectorio dentro del directorio raíz',
  'wizard.rootReadOnly': 'Los directorios raíz los configura un administrador; los usuarios normales solo pueden elegir uno',
  'wizard.subPath': 'Subdirectorio (relativo al raíz)',
  'wizard.subPathHint': 'Se admiten varios niveles, p. ej. team/project; si está vacío se usa el nombre del proyecto',
  'wizard.browse': 'Examinar',
  'wizard.up': 'Directorio superior',
  'wizard.selectThisDir': 'Usar este directorio',
  'wizard.browseEmpty': 'No hay subdirectorios aquí',
  'wizard.noRoots': 'Aún no hay directorios raíz configurados; pide a un administrador',
  'wizard.subPathRequired': 'Introduce un subdirectorio o elige uno en Examinar',
  'wizard.credentialTitle': 'Credenciales de Git (HTTPS)',
  'wizard.gitUser': 'Usuario',
  'wizard.gitSecret': 'Contraseña / token de acceso',
  'wizard.credentialHint': 'Déjalos vacíos para usar las credenciales guardadas; los repositorios públicos pueden dejar ambos vacíos',
  'wizard.credentialUsing': 'Usando credenciales guardadas: {user}',
  'wizard.credentialAnonymous': 'Anónimo (sin credenciales)',
  'wizard.credentialRequired': 'El usuario y la contraseña / token deben rellenarse juntos',
  'wizard.saveCredential': 'Guardar en las credenciales de Git para este host',
  'wizard.gitOk': 'Se puede acceder al repositorio y a las credenciales',
  'wizard.gitVerifyFailed': 'La verificación del repositorio falló',
  'wizard.gitSshUnsupported': 'Las URL SSH aún no son compatibles; usa HTTPS',
  'wizard.targetNotEmpty': 'El directorio de destino no está vacío y no es un repositorio git; elige otro subdirectorio',
  'wizard.summarySource': 'Origen',
  'wizard.summaryRoot': 'Raíz',
  'wizard.summaryTarget': 'Ruta de destino',
  'wizard.summaryName': 'Nombre del proyecto',
  'wizard.summaryCredential': 'Credenciales',

  // ── Git 凭据（pages/settings/GitCredentialsTab.tsx） ────────────────────
  'gitCred.title': 'Credenciales de Git',
  'gitCred.empty': 'Aún no hay credenciales de Git guardadas',
  'gitCred.host': 'Host del repositorio',
  'gitCred.hostHint': 'p. ej. git.example.com o git.example.com:8443; solo HTTPS',
  'gitCred.user': 'Usuario',
  'gitCred.secret': 'Contraseña / token de acceso',
  'gitCred.saved': 'Credencial de Git guardada',
  'gitCred.deleted': 'Credencial de Git eliminada',
  'gitCred.required': 'El usuario y la contraseña / token no pueden estar vacíos',
  'gitCred.invalidHost': 'Introduce un host de repositorio, p. ej. git.example.com',
  'gitCred.deleteOneConfirm':
    '¿Eliminar la credencial de {name}? Los proyectos relacionados la volverán a pedir en la próxima descarga.',
  'gitCred.savedHint': 'Se usa automáticamente al descargar repositorios HTTPS',
  'gitCred.updatedAt': 'Actualizado',
  'gitCred.add': 'Añadir o sobrescribir una credencial',
  'gitCred.plaintextNote': 'La contraseña se guarda en la base de datos local en texto plano y solo se usa para autenticar repositorios HTTPS.',
  'gitCred.adminNote': 'Los administradores pueden ver y eliminar las credenciales de todos los usuarios.',
  'gitCred.dedupNote': 'Una credencial por usuario y host; al guardar de nuevo se sobrescribe la anterior.',
  'gitCred.ownerCount': '{count} usuarios',

  // ── 工作区缓存（pages/settings/DataTab.tsx） ───────────────────────────
  'cache.title': 'Caché del espacio de trabajo',
  'cache.hint': 'Solo cuenta .luban-agent/temp de cada espacio de trabajo; los adjuntos referenciados por sesiones anteriores se conservan por defecto.',
  'cache.keep1': 'Conservar el último día',
  'cache.keep7': 'Conservar los últimos 7 días',
  'cache.keep30': 'Conservar los últimos 30 días',
  'cache.keepAll': 'Sin límite de retención',
  'cache.colWorkspace': 'Espacio de trabajo',
  'cache.colTemp': 'Archivos temporales',
  'cache.colOrphan': 'Adjuntos sin referencia',
  'cache.colReferenced': 'Adjuntos referenciados',
  'cache.colReclaimable': 'Recuperable',
  'cache.clean': 'Limpiar',
  'cache.cleanAll': 'Limpiar todos los espacios',
  'cache.cleaning': 'Limpiando…',
  'cache.total': 'Total {size} · recuperable {reclaimable}',
  'cache.empty': 'No hay caché de espacio de trabajo para limpiar',
  'cache.cleanTitle': 'Limpiar caché del espacio de trabajo — {name}',
  'cache.cleanTitleAll': 'Limpiar la caché de todos los espacios de trabajo',
  'cache.optTemp': 'Eliminar archivos temporales intermedios (miniaturas, imágenes procesadas, resúmenes de conocimiento, restos de herramientas)',
  'cache.optOrphan': 'Eliminar adjuntos no referenciados por ninguna sesión',
  'cache.optReferenced': 'Eliminar adjuntos referenciados por sesiones',
  'cache.optReferencedWarn': 'Estos adjuntos ya no se podrán abrir ni previsualizar en sesiones anteriores y no se podrán recuperar.',
  'cache.estimate': 'Se liberarán unos {size} ({files} archivos); el servidor revisa la retención y las referencias.',
  'cache.cleaned': 'Se limpiaron {size}',
  'cache.cleanedPartial': 'Se limpiaron {size}; no se pudieron eliminar {files} archivos',
} satisfies Record<MessageKey, string>;

export default es;