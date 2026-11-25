// The widgets of BibleStudy on the iPhone (a WidgetKit extension, built by the IPA workflow with swiftc and put into the app's PlugIns folder).
//
// - "BibleStudy" on the lock screen, under the clock: a small round button that only opens the app.
// - "Vers des Tages" on the home screen (small, medium) and the lock screen: the verse of the day and what is next in the reading plan.
//
// The extension cannot read the vault or the Bibles. The app writes a small file (widget.json, see MainWindow.Widgets.cs) into the folder it shares
// with the extension (an app group), with an entry for today and the next two weeks; the widget shows the entry of its day.

import SwiftUI
import WidgetKit

// MARK: - The file the app writes

struct Snapshot: Decodable {
    let verseTitle: String
    let planTitle: String
    let openHint: String
    let noPlan: String
    let plan: PlanInfo?
    let days: [Day]
}

struct PlanInfo: Decodable {
    let name: String
    let read: Int
    let total: Int
}

struct Day: Decodable {
    let date: String
    let ref: String
    let text: String
    let translation: String
    let plan: String?
    let planHint: String?
}

enum SharedFile {
    static let wanted = "group.de.trofline.biblestudy"

    /// SideStore registers the group under the Apple ID that signs the app and may rename it: the real name is in the profile of the installed extension.
    static func groupNames() -> [String] {
        var names: [String] = []
        if let url = Bundle.main.url(forResource: "embedded", withExtension: "mobileprovision"),
           let data = try? Data(contentsOf: url),
           let raw = String(data: data, encoding: .isoLatin1),
           let from = raw.range(of: "<?xml"), let to = raw.range(of: "</plist>"), from.lowerBound < to.upperBound,
           let xml = String(raw[from.lowerBound..<to.upperBound]).data(using: .utf8),
           let profile = try? PropertyListSerialization.propertyList(from: xml, format: nil) as? [String: Any],
           let ent = profile["Entitlements"] as? [String: Any],
           let groups = ent["com.apple.security.application-groups"] as? [String] {
            names.append(contentsOf: groups.filter { $0.contains("de.trofline.biblestudy") })
        }
        names.append(wanted)
        return names
    }

    static func read() -> Snapshot? {
        for name in groupNames() {
            guard let dir = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: name) else { continue }
            if let data = try? Data(contentsOf: dir.appendingPathComponent("widget.json")),
               let snap = try? JSONDecoder().decode(Snapshot.self, from: data) {
                return snap
            }
        }
        return nil
    }
}

// MARK: - Timeline

struct TodayEntry: TimelineEntry {
    let date: Date
    let snapshot: Snapshot?
    let day: Day?
}

struct TodayProvider: TimelineProvider {
    static func key(_ date: Date) -> String {
        let f = DateFormatter()
        f.calendar = Calendar(identifier: .gregorian)
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = .current
        f.dateFormat = "yyyy-MM-dd"
        return f.string(from: date)
    }

    static func entry(_ date: Date, _ snap: Snapshot?) -> TodayEntry {
        let k = key(date)
        return TodayEntry(date: date, snapshot: snap, day: snap?.days.first { $0.date == k })
    }

    func placeholder(in context: Context) -> TodayEntry {
        TodayEntry(date: Date(), snapshot: nil, day: Day(date: "", ref: "Johannes 3:16", text: "Denn also hat Gott die Welt geliebt …", translation: "", plan: nil, planHint: nil))
    }

    func getSnapshot(in context: Context, completion: @escaping (TodayEntry) -> Void) {
        let snap = SharedFile.read()
        completion(snap == nil && context.isPreview ? placeholder(in: context) : Self.entry(Date(), snap))
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<TodayEntry>) -> Void) {
        let snap = SharedFile.read()
        let now = Date()
        let cal = Calendar.current
        // now, and each of the next midnights (the verse changes then); the file is read again every half hour, the app may have written a new one
        var entries = [Self.entry(now, snap)]
        var day = cal.startOfDay(for: now)
        for _ in 0..<3 {
            day = cal.date(byAdding: .day, value: 1, to: day)!
            entries.append(Self.entry(day, snap))
        }
        completion(Timeline(entries: entries, policy: .after(now.addingTimeInterval(30 * 60))))
    }
}

struct LauncherEntry: TimelineEntry { let date: Date }

struct LauncherProvider: TimelineProvider {
    func placeholder(in context: Context) -> LauncherEntry { LauncherEntry(date: Date()) }
    func getSnapshot(in context: Context, completion: @escaping (LauncherEntry) -> Void) { completion(LauncherEntry(date: Date())) }
    func getTimeline(in context: Context, completion: @escaping (Timeline<LauncherEntry>) -> Void) {
        completion(Timeline(entries: [LauncherEntry(date: Date())], policy: .never))
    }
}

// MARK: - Look

/// The colours of the app (light and dark).
extension Color {
    static let bsAccent = Color(UIColor { $0.userInterfaceStyle == .dark ? UIColor(red: 0.89, green: 0.71, blue: 0.37, alpha: 1) : UIColor(red: 0.54, green: 0.31, blue: 0.06, alpha: 1) })
    static let bsBackground = Color(UIColor { $0.userInterfaceStyle == .dark ? UIColor(red: 0.09, green: 0.10, blue: 0.13, alpha: 1) : UIColor(red: 1.0, green: 0.99, blue: 0.96, alpha: 1) })
    static let bsSurface = Color(UIColor { $0.userInterfaceStyle == .dark ? UIColor(red: 0.12, green: 0.15, blue: 0.18, alpha: 1) : UIColor(red: 0.96, green: 0.95, blue: 0.90, alpha: 1) })
}

struct Eyebrow: View {
    let text: String
    var body: some View {
        Text(text.uppercased()).font(.system(size: 10, weight: .bold)).tracking(0.6).foregroundStyle(Color.bsAccent).lineLimit(1)
    }
}

struct TodayView: View {
    @Environment(\.widgetFamily) var family
    let entry: TodayEntry

    var body: some View {
        switch family {
        case .accessoryInline:
            Text(entry.day?.ref ?? "BibleStudy")
        case .accessoryRectangular:
            VStack(alignment: .leading, spacing: 1) {
                Text(entry.day?.ref ?? "BibleStudy").font(.headline).widgetAccentable().lineLimit(1)
                if let plan = entry.day?.plan { Text(plan).font(.caption).lineLimit(2) }
                else if let text = entry.day?.text, !text.isEmpty { Text(text).font(.caption).lineLimit(2) }
                else { Text(entry.snapshot?.openHint ?? "BibleStudy öffnen").font(.caption).lineLimit(2) }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .widgetURL(URL(string: entry.day?.plan != nil ? "biblestudy-widget://plan" : "biblestudy-widget://verse"))
        case .systemMedium:
            HStack(alignment: .top, spacing: 12) {
                verse.frame(maxWidth: .infinity, alignment: .leading)
                if entry.snapshot != nil {
                    Link(destination: URL(string: "biblestudy-widget://plan")!) { plan }
                        .frame(width: 130)
                }
            }
        default:
            verse.widgetURL(URL(string: "biblestudy-widget://verse"))
        }
    }

    var verse: some View {
        VStack(alignment: .leading, spacing: 4) {
            Eyebrow(text: entry.snapshot.map { s in entry.day.map { $0.translation.isEmpty ? s.verseTitle : "\(s.verseTitle) · \($0.translation)" } ?? s.verseTitle } ?? "Vers des Tages")
            if let day = entry.day {
                Text(day.text.isEmpty ? day.ref : day.text).font(.system(.callout, design: .serif)).minimumScaleFactor(0.75)
                Spacer(minLength: 0)
                if !day.text.isEmpty { Text(day.ref).font(.caption2).foregroundStyle(.secondary).lineLimit(1) }
            } else {
                Text(entry.snapshot?.openHint ?? "Öffne BibleStudy einmal, damit das Widget gefüllt wird.").font(.callout).foregroundStyle(.secondary)
                Spacer(minLength: 0)
            }
        }
    }

    var plan: some View {
        VStack(alignment: .leading, spacing: 4) {
            Eyebrow(text: entry.snapshot?.planTitle ?? "Heute dran")
            if let p = entry.snapshot?.plan, let label = entry.day?.plan {
                Text(label).font(.subheadline.weight(.semibold)).lineLimit(3).minimumScaleFactor(0.8)
                Spacer(minLength: 0)
                ProgressView(value: Double(p.read), total: Double(max(1, p.total))).tint(Color.bsAccent)
                Text("\(entry.day?.planHint ?? p.name) · \(p.total > 0 ? Int((100.0 * Double(p.read) / Double(p.total)).rounded()) : 0) %")
                    .font(.caption2).foregroundStyle(.secondary).lineLimit(2)
            } else {
                Text(entry.snapshot?.noPlan ?? "").font(.caption).foregroundStyle(.secondary).lineLimit(4)
                Spacer(minLength: 0)
            }
        }
        .padding(10)
        .frame(maxHeight: .infinity, alignment: .topLeading)
        .background(Color.bsSurface, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
    }
}

struct LauncherView: View {
    @Environment(\.widgetFamily) var family
    var body: some View {
        switch family {
        case .accessoryInline:
            Label("BibleStudy", systemImage: "book.closed.fill")
        default:
            ZStack {
                AccessoryWidgetBackground()
                Image(systemName: "book.closed.fill").font(.system(size: 22, weight: .semibold)).widgetAccentable()
            }
        }
    }
}

// MARK: - Widgets

struct TodayWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "BibleStudyToday", provider: TodayProvider()) { entry in
            TodayView(entry: entry).containerBackground(for: .widget) { Color.bsBackground }
        }
        .configurationDisplayName("Vers des Tages")
        .description("Der Vers des Tages und die heutige Lesung aus deinem Leseplan.")
        .supportedFamilies([.systemSmall, .systemMedium, .accessoryRectangular, .accessoryInline])
    }
}

struct LauncherWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "BibleStudyLauncher", provider: LauncherProvider()) { _ in
            LauncherView().containerBackground(for: .widget) { Color.clear }.widgetURL(URL(string: "biblestudy-widget://open"))
        }
        .configurationDisplayName("BibleStudy öffnen")
        .description("Ein Knopf unter der Uhr, der BibleStudy startet.")
        .supportedFamilies([.accessoryCircular, .accessoryInline])
    }
}

@main
struct BibleStudyWidgets: WidgetBundle {
    var body: some Widget {
        LauncherWidget()
        TodayWidget()
    }
}
