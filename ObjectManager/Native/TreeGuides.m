#import <Cocoa/Cocoa.h>

@interface OOTreeGuides : NSView
@property(nonatomic, weak) NSOutlineView *tree;
@end

@implementation OOTreeGuides
- (BOOL)isFlipped { return YES; }
- (BOOL)isAccessibilityElement { return NO; }
- (NSView *)hitTest:(NSPoint)point { return nil; }
- (void)refresh:(NSNotification *)note {
    NSOutlineView *tree = self.tree;
    if (!tree) return;
    self.frame = self.superview.bounds;
    self.needsDisplay = YES;
}
- (void)drawRect:(NSRect)dirty {
    NSOutlineView *tree = self.tree;
    id<NSOutlineViewDataSource> source = tree.dataSource;
    if (!source || !tree.outlineTableColumn) return;
    NSRange rows = [tree rowsInRect:tree.visibleRect];
    if (!rows.length || rows.location == NSNotFound) return;
    NSInteger column = [tree.tableColumns indexOfObjectIdenticalTo:tree.outlineTableColumn];
    if (column == NSNotFound) return;
    [NSBezierPath clipRect:[self convertRect:[tree rectOfColumn:column] fromView:tree]];
    NSBezierPath *lines = [NSBezierPath bezierPath];
    lines.lineWidth = 1.0 / MAX(1.0, self.window.backingScaleFactor);
    NSHashTable *seen = [NSHashTable hashTableWithOptions:NSPointerFunctionsObjectPointerPersonality];
    for (NSUInteger row = rows.location; row < NSMaxRange(rows); row++) {
        id child = [tree itemAtRow:row];
        id parent = child ? [tree parentForItem:child] : nil;
        BOOL direct = YES;
        while (parent) {
            NSInteger pr = [tree rowForItem:parent];
            if (pr < 0) break;
            NSRect arrow = [tree frameOfOutlineCellAtRow:pr];
            if (NSIsEmptyRect(arrow)) break;
            CGFloat x = NSMidX(arrow);
            if (direct) {
                CGFloat y = NSMidY([tree rectOfRow:row]);
                [lines moveToPoint:[self convertPoint:NSMakePoint(x,y) fromView:tree]];
                [lines lineToPoint:[self convertPoint:NSMakePoint(x + tree.indentationPerLevel * 0.65,y) fromView:tree]];
            }
            if ([seen containsObject:parent]) break;
            [seen addObject:parent];
            NSInteger count = [source outlineView:tree numberOfChildrenOfItem:parent];
            if (count > 0) {
                id last = [source outlineView:tree child:count-1 ofItem:parent];
                NSInteger end = [tree rowForItem:last];
                if (end > pr) {
                    [lines moveToPoint:[self convertPoint:NSMakePoint(x,NSMaxY(arrow)) fromView:tree]];
                    [lines lineToPoint:[self convertPoint:NSMakePoint(x,NSMidY([tree rectOfRow:end])) fromView:tree]];
                }
            }
            direct = NO;
            parent = [tree parentForItem:parent];
        }
    }
    NSColor *tint = nil;
    for (NSUInteger row = rows.location; row < NSMaxRange(rows) && !tint; row++) {
        NSTableRowView *rv = [tree rowViewAtRow:row makeIfNecessary:NO];
        for (NSView *view in rv.subviews) {
            if ([view.identifier isEqualToString:NSOutlineViewDisclosureButtonKey] &&
                [view isKindOfClass:NSButton.class]) {
                tint = ((NSButton *)view).contentTintColor;
                break;
            }
        }
    }
    [(tint ?: NSColor.secondaryLabelColor) setStroke];
    [lines stroke];
}
@end

void *OOAttachGuides(void *handle) {
    NSOutlineView *tree = (__bridge NSOutlineView *)handle;
    if (![tree isKindOfClass:NSOutlineView.class]) return NULL;
    NSClipView *clip = tree.enclosingScrollView.contentView;
    NSView *document = clip.documentView;
    if (!document || document.superview != clip ||
        (document != tree && ![tree isDescendantOf:document])) return NULL;
    OOTreeGuides *view = [[OOTreeGuides alloc] initWithFrame:clip.bounds];
    view.tree = tree;
    // Row views are recycled inside the document; keep guides above that entire subtree.
    [clip addSubview:view positioned:NSWindowAbove relativeTo:document];
    clip.postsBoundsChangedNotifications = YES;
    NSNotificationCenter *nc = NSNotificationCenter.defaultCenter;
    [nc addObserver:view selector:@selector(refresh:) name:NSViewBoundsDidChangeNotification object:clip];
    for (NSNotificationName name in @[NSOutlineViewItemDidExpandNotification, NSOutlineViewItemDidCollapseNotification,
        NSTableViewColumnDidResizeNotification, NSTableViewColumnDidMoveNotification]) {
        [nc addObserver:view selector:@selector(refresh:) name:name object:tree];
    }
    return (__bridge_retained void *)view;
}
void OORefreshGuides(void *handle) { [(__bridge OOTreeGuides *)handle refresh:nil]; }
void OODetachGuides(void *handle) {
    OOTreeGuides *view = (__bridge_transfer OOTreeGuides *)handle;
    [NSNotificationCenter.defaultCenter removeObserver:view];
    [view removeFromSuperview];
}
