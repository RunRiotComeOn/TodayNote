#import <Cocoa/Cocoa.h>
#import <WebKit/WebKit.h>

@interface AppDelegate : NSObject <NSApplicationDelegate, NSWindowDelegate, WKScriptMessageHandler, WKNavigationDelegate>
@property(nonatomic,strong) NSWindow *window;
@property(nonatomic,strong) WKWebView *webView;
@property(nonatomic,strong) NSStatusItem *statusItem;
@property(nonatomic,strong) NSURL *dataURL;
@property(nonatomic,strong) NSURL *htmlURL;
@property(nonatomic,copy) NSString *notice;
@property(nonatomic) BOOL quitApproved;
@property(nonatomic) BOOL waitingToQuit;
@end

@implementation AppDelegate

- (void)showAlert:(NSString *)message {
    NSAlert *alert = [[NSAlert alloc] init];
    alert.messageText = @"今日";
    alert.informativeText = message ?: @"这一步没有完成。";
    [alert addButtonWithTitle:@"确定"];
    [alert runModal];
}

- (NSURL *)startupURL {
    return [NSURL fileURLWithPath:[NSHomeDirectory() stringByAppendingPathComponent:@"Library/LaunchAgents/local.todaynote.desktop.plist"]];
}

- (BOOL)startupEnabled { return [[NSFileManager defaultManager] fileExistsAtPath:self.startupURL.path]; }

- (BOOL)setStartup:(BOOL)enabled error:(NSError **)error {
    NSURL *url = self.startupURL;
    if (!enabled) {
        if (!self.startupEnabled) return YES;
        return [[NSFileManager defaultManager] removeItemAtURL:url error:error];
    }
    if (![[NSFileManager defaultManager] createDirectoryAtURL:[url URLByDeletingLastPathComponent] withIntermediateDirectories:YES attributes:nil error:error]) return NO;
    NSDictionary *plist = @{ @"Label":@"local.todaynote.desktop", @"ProgramArguments":@[NSBundle.mainBundle.executablePath], @"RunAtLoad":@YES, @"ProcessType":@"Interactive" };
    NSData *data = [NSPropertyListSerialization dataWithPropertyList:plist format:NSPropertyListXMLFormat_v1_0 options:0 error:error];
    return data && [data writeToURL:url options:NSDataWritingAtomic error:error];
}

- (BOOL)validateState:(id)state error:(NSError **)error {
    BOOL valid = [state isKindOfClass:NSDictionary.class] && [state[@"Version"] isEqual:@1] && [state[@"Settings"] isKindOfClass:NSDictionary.class] && [state[@"Tasks"] isKindOfClass:NSArray.class];
    if (valid) {
        NSArray *tasks = state[@"Tasks"];
        valid = tasks.count <= 100000;
        NSMutableSet *ids = [NSMutableSet set];
        for (id task in tasks) {
            if (![task isKindOfClass:NSDictionary.class]) { valid = NO; break; }
            id title = task[@"Title"], identifier = task[@"Id"];
            if (![title isKindOfClass:NSString.class] || ![identifier isKindOfClass:NSString.class] || [identifier length] == 0 || [ids containsObject:identifier] || [title length] > 500 || [[title stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet] length] == 0) { valid = NO; break; }
            [ids addObject:identifier];
        }
    }
    if (!valid && error) *error = [NSError errorWithDomain:@"TodayNote" code:1 userInfo:@{NSLocalizedDescriptionKey:@"待办或备份格式不受支持，原文件已保留。"}];
    return valid;
}

- (id)readState:(NSURL *)url error:(NSError **)error {
    NSData *data = [NSData dataWithContentsOfURL:url options:0 error:error];
    if (!data) return nil;
    if (data.length > 16 * 1024 * 1024) {
        if (error) *error = [NSError errorWithDomain:@"TodayNote" code:2 userInfo:@{NSLocalizedDescriptionKey:@"备份文件超过 16 MB。"}];
        return nil;
    }
    id state = [NSJSONSerialization JSONObjectWithData:data options:0 error:error];
    return state && [self validateState:state error:error] ? state : nil;
}

- (id)loadState:(NSError **)error {
    NSFileManager *fm = NSFileManager.defaultManager;
    NSURL *backup = [NSURL fileURLWithPath:[self.dataURL.path stringByAppendingString:@".bak"]];
    BOOL primaryExists = [fm fileExistsAtPath:self.dataURL.path];
    if (!primaryExists && ![fm fileExistsAtPath:backup.path]) return NSNull.null;
    NSError *readError = nil;
    id state = primaryExists ? [self readState:self.dataURL error:&readError] : nil;
    if (state) return state;
    state = [self readState:backup error:error];
    if (!state) { if (error && readError) *error = readError; return nil; }
    if (primaryExists) {
        NSString *name = [NSString stringWithFormat:@"unreadable-%@.json", NSUUID.UUID.UUIDString];
        NSURL *preserved = [[self.dataURL URLByDeletingLastPathComponent] URLByAppendingPathComponent:name];
        if (![fm moveItemAtURL:self.dataURL toURL:preserved error:error]) return nil;
    }
    self.notice = @"已恢复最近一次可读取的本地备份。无法读取的原文件也已保留。";
    return state;
}

- (BOOL)writeState:(id)state toURL:(NSURL *)url backup:(BOOL)backup error:(NSError **)error {
    if (![self validateState:state error:error]) return NO;
    NSData *data = [NSJSONSerialization dataWithJSONObject:state options:0 error:error];
    if (!data) return NO;
    if (data.length > 16 * 1024 * 1024) {
        if (error) *error = [NSError errorWithDomain:@"TodayNote" code:2 userInfo:@{NSLocalizedDescriptionKey:@"数据超过 16 MB，请先导出备份。"}];
        return NO;
    }
    NSFileManager *fm = NSFileManager.defaultManager;
    if (![fm createDirectoryAtURL:[url URLByDeletingLastPathComponent] withIntermediateDirectories:YES attributes:nil error:error]) return NO;
    if (backup && [fm fileExistsAtPath:url.path]) {
        NSData *old = [NSData dataWithContentsOfURL:url options:0 error:error];
        if (!old || ![old writeToURL:[NSURL fileURLWithPath:[url.path stringByAppendingString:@".bak"]] options:NSDataWritingAtomic error:error]) return NO;
    }
    return [data writeToURL:url options:NSDataWritingAtomic error:error];
}

- (void)sendReply:(NSNumber *)identifier data:(id)data error:(NSError *)error {
    NSDictionary *message = error ? @{ @"id":identifier, @"ok":@NO, @"error":error.localizedDescription ?: @"这一步没有完成。" } : @{ @"id":identifier, @"ok":@YES, @"data":data ?: NSNull.null };
    NSData *encoded = [NSJSONSerialization dataWithJSONObject:message options:0 error:nil];
    NSString *json = [[NSString alloc] initWithData:encoded encoding:NSUTF8StringEncoding];
    [self.webView evaluateJavaScript:[NSString stringWithFormat:@"window.TodayApp.receive(%@)", json] completionHandler:nil];
}

- (void)userContentController:(WKUserContentController *)controller didReceiveScriptMessage:(WKScriptMessage *)message {
    if (!message.frameInfo.mainFrame || ![message.body isKindOfClass:NSDictionary.class]) return;
    NSDictionary *body = message.body;
    NSNumber *identifier = body[@"id"];
    NSString *action = body[@"action"];
    NSDictionary *args = [body[@"args"] isKindOfClass:NSDictionary.class] ? body[@"args"] : @{};
    if (![identifier isKindOfClass:NSNumber.class] || ![action isKindOfClass:NSString.class]) return;
    NSError *error = nil;
    id result = NSNull.null;
    if ([action isEqualToString:@"load"]) {
        id state = [self loadState:&error];
        if (state) result = @{ @"state":state, @"startup":@(self.startupEnabled), @"notice":self.notice ?: @"" };
    } else if ([action isEqualToString:@"save"]) {
        result = @([self writeState:args[@"state"] toURL:self.dataURL backup:YES error:&error]);
    } else if ([action isEqualToString:@"options"]) {
        NSString *theme = args[@"theme"];
        NSApp.appearance = [theme isEqual:@"dark"] ? [NSAppearance appearanceNamed:NSAppearanceNameDarkAqua] : [theme isEqual:@"light"] ? [NSAppearance appearanceNamed:NSAppearanceNameAqua] : nil;
        self.window.level = [args[@"topmost"] boolValue] ? NSFloatingWindowLevel : NSNormalWindowLevel;
        self.window.collectionBehavior = NSWindowCollectionBehaviorCanJoinAllSpaces | NSWindowCollectionBehaviorFullScreenAuxiliary;
        self.window.backgroundColor = NSColor.windowBackgroundColor;
    } else if ([action isEqualToString:@"startup"]) {
        if ([self setStartup:[args[@"enabled"] boolValue] error:&error]) result = @(self.startupEnabled);
    } else if ([action isEqualToString:@"alert"]) {
        [self showAlert:[args[@"message"] isKindOfClass:NSString.class] ? args[@"message"] : @"这一步没有完成。"];
    } else if ([action isEqualToString:@"export"]) {
        NSSavePanel *panel = NSSavePanel.savePanel;
        NSDateFormatter *format = [[NSDateFormatter alloc] init]; format.dateFormat = @"yyyyMMdd-HHmmss";
        panel.nameFieldStringValue = [NSString stringWithFormat:@"TodayNote-%@.json", [format stringFromDate:NSDate.date]];
        panel.allowedFileTypes = @[@"json"];
        result = @NO;
        if ([panel runModal] == NSModalResponseOK) result = @([self writeState:args[@"state"] toURL:panel.URL backup:NO error:&error]);
    } else if ([action isEqualToString:@"import"]) {
        NSOpenPanel *panel = NSOpenPanel.openPanel; panel.allowedFileTypes = @[@"json"]; panel.allowsMultipleSelection = NO; panel.canChooseDirectories = NO;
        if ([panel runModal] == NSModalResponseOK) result = [self readState:panel.URL error:&error];
    } else if ([action isEqualToString:@"restore"]) {
        NSURL *snapshot = [[self.dataURL URLByDeletingLastPathComponent] URLByAppendingPathComponent:[NSString stringWithFormat:@"before-import-%@.json", NSUUID.UUID.UUIDString]];
        if ([self writeState:args[@"previous"] toURL:snapshot backup:NO error:&error])
            result = @([self writeState:args[@"state"] toURL:self.dataURL backup:YES error:&error]);
    } else if ([action isEqualToString:@"quit"]) {
        self.quitApproved = YES;
        if (self.waitingToQuit) [NSApp replyToApplicationShouldTerminate:YES]; else [NSApp terminate:nil];
        return;
    } else if ([action isEqualToString:@"cancelQuit"]) {
        if (self.waitingToQuit) { self.waitingToQuit = NO; [NSApp replyToApplicationShouldTerminate:NO]; }
    } else error = [NSError errorWithDomain:@"TodayNote" code:3 userInfo:@{NSLocalizedDescriptionKey:@"无法完成这项操作。"}];
    [self sendReply:identifier data:result error:error];
}

- (void)webView:(WKWebView *)webView decidePolicyForNavigationAction:(WKNavigationAction *)action decisionHandler:(void (^)(WKNavigationActionPolicy))decisionHandler {
    NSURL *url = action.request.URL;
    BOOL allowed = url.isFileURL && [url.path isEqualToString:self.htmlURL.path];
    decisionHandler(allowed ? WKNavigationActionPolicyAllow : WKNavigationActionPolicyCancel);
}

- (void)webView:(WKWebView *)webView didFailProvisionalNavigation:(WKNavigation *)navigation withError:(NSError *)error {
    [self showAlert:[@"暂时无法打开今日。\n" stringByAppendingString:error.localizedDescription]];
}

- (void)showWindow:(id)sender {
    [NSApp activateIgnoringOtherApps:YES];
    [self.window deminiaturize:nil]; [self.window makeKeyAndOrderFront:nil];
}
- (void)openSettings:(id)sender { [self showWindow:nil]; [self.webView evaluateJavaScript:@"window.TodayApp.openSettings()" completionHandler:nil]; }
- (void)newTask:(id)sender { [self showWindow:nil]; [self.webView evaluateJavaScript:@"window.TodayApp.focusInput()" completionHandler:nil]; }
- (void)togglePin:(id)sender { [self.webView evaluateJavaScript:@"window.TodayApp.togglePin()" completionHandler:nil]; }
- (void)requestQuit:(id)sender {
    [NSApp terminate:nil];
}
- (NSApplicationTerminateReply)applicationShouldTerminate:(NSApplication *)sender {
    if (self.quitApproved || !self.webView) return NSTerminateNow;
    if (!self.waitingToQuit) {
        self.waitingToQuit = YES;
        [self.webView evaluateJavaScript:@"window.TodayApp.requestQuit(); void 0;" completionHandler:^(id value,NSError *error){
            if (error && self.waitingToQuit) { self.quitApproved = YES; [NSApp replyToApplicationShouldTerminate:YES]; }
        }];
    }
    return NSTerminateLater;
}
- (BOOL)windowShouldClose:(NSWindow *)sender { [sender orderOut:nil]; return NO; }
- (BOOL)applicationShouldTerminateAfterLastWindowClosed:(NSApplication *)sender { return NO; }
- (BOOL)applicationShouldHandleReopen:(NSApplication *)sender hasVisibleWindows:(BOOL)flag { [self showWindow:nil]; return YES; }
- (void)windowDidMove:(NSNotification *)notification { [self.window saveFrameUsingName:@"TodayNote.Main"]; }
- (void)windowDidResize:(NSNotification *)notification { [self.window saveFrameUsingName:@"TodayNote.Main"]; }

- (NSMenuItem *)item:(NSString *)title action:(SEL)selector key:(NSString *)key {
    NSMenuItem *item = [[NSMenuItem alloc] initWithTitle:title action:selector keyEquivalent:key]; item.target = self; return item;
}

- (void)installMenus {
    NSMenu *bar = [[NSMenu alloc] init];
    NSMenuItem *appItem = [[NSMenuItem alloc] init]; [bar addItem:appItem];
    NSMenu *appMenu = [[NSMenu alloc] initWithTitle:@"今日"];
    [appMenu addItem:[self item:@"打开今日" action:@selector(showWindow:) key:@"0"]];
    [appMenu addItem:[self item:@"设置…" action:@selector(openSettings:) key:@","]];
    [appMenu addItem:NSMenuItem.separatorItem];
    [appMenu addItem:[self item:@"退出今日" action:@selector(requestQuit:) key:@"q"]]; appItem.submenu = appMenu;
    NSMenuItem *fileItem = [[NSMenuItem alloc] initWithTitle:@"文件" action:nil keyEquivalent:@""]; [bar addItem:fileItem];
    NSMenu *file = [[NSMenu alloc] initWithTitle:@"文件"]; [file addItem:[self item:@"新待办" action:@selector(newTask:) key:@"n"]]; fileItem.submenu = file;
    NSMenuItem *editItem = [[NSMenuItem alloc] initWithTitle:@"编辑" action:nil keyEquivalent:@""]; [bar addItem:editItem];
    NSMenu *edit = [[NSMenu alloc] initWithTitle:@"编辑"];
    NSArray *titles = @[@"撤销",@"重做",@"剪切",@"复制",@"粘贴",@"全选"];
    NSArray *selectors = @[@"undo:",@"redo:",@"cut:",@"copy:",@"paste:",@"selectAll:"];
    NSArray *keys = @[@"z",@"Z",@"x",@"c",@"v",@"a"];
    for (NSUInteger n=0;n<titles.count;n++) { NSMenuItem *item = [[NSMenuItem alloc] initWithTitle:titles[n] action:NSSelectorFromString(selectors[n]) keyEquivalent:keys[n]]; [edit addItem:item]; }
    editItem.submenu = edit; NSApp.mainMenu = bar;
    self.statusItem = [NSStatusBar.systemStatusBar statusItemWithLength:NSSquareStatusItemLength];
    NSImage *image = [NSImage imageWithSystemSymbolName:@"checkmark.circle" accessibilityDescription:@"今日"]; image.template = YES;
    self.statusItem.button.image = image;
    self.statusItem.button.toolTip = @"今日 · 待办便利贴";
    NSMenu *status = [[NSMenu alloc] init];
    [status addItem:[self item:@"打开今日" action:@selector(showWindow:) key:@""]];
    [status addItem:[self item:@"设置…" action:@selector(openSettings:) key:@""]];
    [status addItem:[self item:@"切换置顶" action:@selector(togglePin:) key:@""]];
    [status addItem:NSMenuItem.separatorItem]; [status addItem:[self item:@"退出" action:@selector(requestQuit:) key:@""]];
    self.statusItem.menu = status;
}

- (void)applicationDidFinishLaunching:(NSNotification *)notification {
    [self installMenus];
    NSFileManager *fm = NSFileManager.defaultManager;
    NSURL *support = [[fm URLsForDirectory:NSApplicationSupportDirectory inDomains:NSUserDomainMask] firstObject];
    self.dataURL = [[support URLByAppendingPathComponent:@"TodayNoteMac" isDirectory:YES] URLByAppendingPathComponent:@"data.json"];
    if (self.startupEnabled) [self setStartup:YES error:nil];
    self.htmlURL = [NSBundle.mainBundle URLForResource:@"index" withExtension:@"html"];
    if (!self.htmlURL) { [self showAlert:@"应用文件不完整，请重新解压。" ]; [NSApp terminate:nil]; return; }
    self.window = [[NSWindow alloc] initWithContentRect:NSMakeRect(0,0,400,640) styleMask:NSWindowStyleMaskTitled | NSWindowStyleMaskClosable | NSWindowStyleMaskMiniaturizable | NSWindowStyleMaskResizable | NSWindowStyleMaskFullSizeContentView backing:NSBackingStoreBuffered defer:NO];
    self.window.title = @"今日"; self.window.titleVisibility = NSWindowTitleHidden; self.window.titlebarAppearsTransparent = YES;
    self.window.minSize = NSMakeSize(340,460); self.window.maxSize = NSMakeSize(900,1200); self.window.delegate = self;
    self.window.releasedWhenClosed = NO; self.window.level = NSFloatingWindowLevel;
    [self.window setFrameAutosaveName:@"TodayNote.Main"];
    if (![self.window setFrameUsingName:@"TodayNote.Main"]) [self.window center];
    // A removed external display must not leave the note unreachable.
    BOOL onScreen = NO; for (NSScreen *screen in NSScreen.screens) if (NSIntersectsRect(screen.visibleFrame,self.window.frame)) onScreen = YES;
    if (!onScreen) [self.window center];
    WKWebViewConfiguration *config = [[WKWebViewConfiguration alloc] init];
    config.websiteDataStore = WKWebsiteDataStore.nonPersistentDataStore;
    [config.userContentController addScriptMessageHandler:self name:@"todayNote"];
    self.webView = [[WKWebView alloc] initWithFrame:self.window.contentView.bounds configuration:config];
    self.webView.autoresizingMask = NSViewWidthSizable | NSViewHeightSizable; self.webView.navigationDelegate = self;
    [self.window.contentView addSubview:self.webView];
    [self.webView loadFileURL:self.htmlURL allowingReadAccessToURL:[self.htmlURL URLByDeletingLastPathComponent]];
    [self showWindow:nil];
}
@end

int main(int argc, const char *argv[]) {
    @autoreleasepool {
        NSArray *running = [NSRunningApplication runningApplicationsWithBundleIdentifier:NSBundle.mainBundle.bundleIdentifier];
        for (NSRunningApplication *other in running) {
            if (other.processIdentifier != NSProcessInfo.processInfo.processIdentifier && !other.terminated) {
                [other activateWithOptions:NSApplicationActivateIgnoringOtherApps]; return 0;
            }
        }
        NSApplication *app = NSApplication.sharedApplication;
        __attribute__((objc_precise_lifetime)) AppDelegate *delegate = [[AppDelegate alloc] init];
        app.delegate = delegate; [app setActivationPolicy:NSApplicationActivationPolicyRegular]; [app run];
    }
    return 0;
}
