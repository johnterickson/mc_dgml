using System.Text.Json;

Dictionary<string, Node> nodes = new();

Node GetOrCreateNode(string id)
{
    if (!nodes.ContainsKey(id))
    {
        nodes[id] = new Node(id);
    }

    return nodes[id];
}

List<Packet> packets = new();
int i;
for(i = 0; i < args.Length; i++)
{
    if (args[i].StartsWith("--"))
    {
        break;
    }

    string dir = (Path.IsPathRooted(args[i]) ? Path.GetDirectoryName(args[i]) : null) ?? Environment.CurrentDirectory;
    string pattern = Path.GetFileName(args[i]);
    Console.WriteLine($"Loading packets from {dir} with pattern {pattern}");
    foreach (var file in Directory.EnumerateFiles(dir, pattern))
    {
        Console.WriteLine($" Found file: {file}");
        var content = File.ReadAllText(file);
        Console.WriteLine($"  File size: {content.Length} bytes");
        var deserializedPackets = JsonSerializer.Deserialize<Packet[]>(content);
        Console.WriteLine($"  Deserialized {deserializedPackets?.Length ?? 0} packets");
        packets.AddRange(deserializedPackets!);
    }
}

if (packets.Count == 0)
{
    Console.WriteLine("No packets found");
    return;
}

foreach(var packet in packets!)
{
    string observer = packet.origin_id.Substring(0,2).ToLowerInvariant();
    packet.path.Add(observer);

    GetOrCreateNode(observer).IsObserver = true;

    string sender = packet.path.First();

    foreach(var receiver in packet.path.Skip(1))
    {
        if (sender != receiver)
        {
            var senderNode = GetOrCreateNode(sender);
            senderNode.GetOrCreateOutgoingLink(receiver).UniquePackets.Add(packet.hash);
            senderNode.UniquePacketsSent.Add(packet.hash);

            var receiverNode = GetOrCreateNode(receiver);
            receiverNode.GetOrCreateIncomingLink(sender).UniquePackets.Add(packet.hash);
            receiverNode.UniquePacketsReceived.Add(packet.hash);
        }

        sender = receiver;
    }
}

foreach(var node in nodes.Values)
{
    if (node.OutgoingLinks.Count == 0)
    {
        continue;
    }

    int totalWeight = node.OutgoingLinks.Values.Max(l => l.UniquePackets.Count);
    foreach(var link in node.OutgoingLinks.Values)
    {
        link.NormalizedWeight = (double)link.UniquePackets.Count / totalWeight;
    }
}

foreach(var node in nodes.Values)
{
    if (node.IncomingLinks.Count == 0)
    {
        continue;
    }
    
    int totalWeight = node.IncomingLinks.Values.Max(l => l.UniquePackets.Count);
    foreach(var link in node.IncomingLinks.Values)  
    {
        link.NormalizedWeight = (double)link.UniquePackets.Count / totalWeight;
    }
}

// summaries
foreach(var node in nodes.Values)
{
    node.UniqueLinks = new HashSet<string>();
    node.UniqueLinks.UnionWith(node.OutgoingLinks.Keys);
    node.UniqueLinks.UnionWith(node.IncomingLinks.Keys);

    node.UniquePackets = new HashSet<string>();
    node.UniquePackets.UnionWith(node.UniquePacketsReceived);
    node.UniquePackets.UnionWith(node.UniquePacketsSent);
}

if (args[i] == "--open")
{
    foreach(var prefix in Enumerable.Range(0, 256).Select(x => x.ToString("x2")))
    {
        if (nodes.TryGetValue(prefix, out var node))
        {
            Console.WriteLine($"Node {prefix}: {node.UniquePackets.Count} packets seen. {node.UniqueLinks.Count} links.");
        }
        else
        {
            Console.WriteLine($"Node {prefix}: AVAILABLE");
        }
    }
}
else if (args[i] == "--route")
{
    i++;
    string start = args[i];
    i++;
    List<string> wholeRoute = new();

    while(i < args.Length)
    {
        string endId = args[i];

        // BFS
        Queue<(string nodeId, List<string> path)> queue = new();
        queue.Enqueue((start, new List<string> { start }));
        HashSet<string> visited = new();
        bool found = false;
        while (queue.Count > 0)
        {
            var (nodeId, path) = queue.Dequeue();
            if (nodeId == endId)
            {
                if (wholeRoute.Count > 0)
                {
                    wholeRoute.AddRange(path.Skip(1));
                }
                else
                {
                    wholeRoute.AddRange(path);
                }
                found = true;
                break;
            }
            // Console.WriteLine($"Visiting {node}");

            visited.Add(nodeId);
            if (nodes.TryGetValue(nodeId, out var node))
            {
                foreach (var neighbor in node.OutgoingLinks.OrderByDescending(n => n.Value.NormalizedWeight).Select(n => n.Key))
                {
                    // Console.WriteLine($"Neighbor of {node}: {neighbor}");
                    if (!visited.Contains(neighbor))
                    {
                        var newPath = new List<string>(path) { neighbor };
                        queue.Enqueue((neighbor, newPath));
                    }
                }
            }
        }

        if (found)
        {
            start = endId;
            i++;
        }
        else
        {
            Console.WriteLine("No route found");
            return;
        }
    }

    Console.WriteLine(string.Join(",", wholeRoute));
}
else if (args[i] == "--mqttdgml")
{
    string fileName = args.Length > i + 1 ? args[i + 1] : "mc.dgml";
    using var writer = new StreamWriter(fileName);
    writer.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
    writer.WriteLine("<DirectedGraph Title=\"MC\" xmlns=\"http://schemas.microsoft.com/vs/2009/dgml\">");
    writer.WriteLine("<Nodes>");
    var mqttNodes = nodes.Values.Where(n => n.IsObserver).ToDictionary(n => n.Id, n => n);
    HashSet<string> allPackets = new();
    foreach(var node in mqttNodes.Values)
    {
        allPackets.UnionWith(node.UniquePacketsReceived);
    }
    foreach(var node in mqttNodes.Values)
    {
        const int maxStrokeThickness = 10;
        double fractionOfPackets = node.UniquePacketsReceived.Count * 1.0 / allPackets.Count;
        int strokeThickness = Math.Clamp((int)(fractionOfPackets * maxStrokeThickness), 1, maxStrokeThickness);
        writer.WriteLine($"  <Node Id=\"{node.Id}\" Label=\"{node.Id}: -&gt;{node.UniquePacketsReceived.Count} &lt;-{node.UniquePacketsSent.Count}\" "
            + $"StrokeThickness=\"{strokeThickness}\" "
            + (node.IsObserver ? "Background=\"Blue\"" : "") 
            + " />");
    }
    writer.WriteLine("</Nodes>");
    writer.WriteLine("<Links>");
    foreach(var node1 in mqttNodes.Values)
    {
        foreach(var node2 in mqttNodes.Values)
        {
            if (string.CompareOrdinal(node1.Id, node2.Id) >= 0)
            {
                continue;
            }

            const int maxStrokeThickness = 10;
            HashSet<string> packetsInCommon = new();
            packetsInCommon.UnionWith(node1.UniquePacketsReceived);
            packetsInCommon.IntersectWith(node2.UniquePacketsReceived);

            if (packetsInCommon.Count == 0)
            {
                continue;
            }

            double fractionOfPackets = packetsInCommon.Count * 1.0 / allPackets.Count;
            int strokeThickness = Math.Clamp((int)(fractionOfPackets * maxStrokeThickness), 1, maxStrokeThickness);;
            writer.WriteLine($"  <Link Source=\"{node1.Id}\" Target=\"{node2.Id}\" Label=\"{packetsInCommon.Count}\" StrokeThickness=\"{strokeThickness}\"/>");
        }
    }
    writer.WriteLine("</Links>");
    writer.WriteLine("</DirectedGraph>");
}
else if (args[i] == "--dgml")
{
    string fileName = args.Length > i + 1 ? args[i + 1] : "mc.dgml";
    using var writer = new StreamWriter(fileName);
    writer.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
    writer.WriteLine("<DirectedGraph Title=\"MC\" xmlns=\"http://schemas.microsoft.com/vs/2009/dgml\">");
    writer.WriteLine("<Nodes>");
    foreach(var node in nodes.Values)
    {
        writer.WriteLine($"  <Node Id=\"{node.Id}\" Label=\"{node.Id}: -&gt;{node.UniquePacketsReceived.Count} &lt;-{node.UniquePacketsSent.Count}\" "
            + (node.IsObserver ? "Background=\"Blue\"" : "") 
            + " />");
    }
    writer.WriteLine("</Nodes>");
    writer.WriteLine("<Links>");
    foreach(var node in nodes.Values)
    {
        foreach(var link in node.OutgoingLinks.Values)
        {
            const int maxStrokeThickness = 4;
            int strokeThickness = Math.Clamp((int)(link.NormalizedWeight * maxStrokeThickness), 1, maxStrokeThickness);;
            writer.WriteLine($"  <Link Source=\"{node.Id}\" Target=\"{link.OtherId}\" Label=\"{link.UniquePackets.Count}\" StrokeThickness=\"{strokeThickness}\"/>");
        }
    }
    writer.WriteLine("</Links>");
    writer.WriteLine("</DirectedGraph>");
}
else if (args[i] == "--print")
{
    foreach(var packet in packets.OrderBy(p => p.hash))
    {
        Console.WriteLine($"{packet.hash}: " + string.Join(",", packet.path));
    }
}
else
{
    Console.WriteLine($"Don't know how to handle argument {args[0]}");
}




struct Packet
{
    public string hash {get;set;}
    public List<string> path {get;set;}
    public string origin_id {get;set;}
}

class Link
{
    public string OtherId {get;set;}

    public Link(string otherId)
    {
        OtherId = otherId;
    }

    public HashSet<string> UniquePackets = new();
    public double NormalizedWeight {get;set;}
}

class Node
{
    public HashSet<string> UniquePacketsSent = new();
    public HashSet<string> UniquePacketsReceived = new();
    public string Id {get;set;}
    public bool IsObserver { get; internal set; }
    public HashSet<string> UniqueLinks { get; internal set; }
    public HashSet<string> UniquePackets { get; internal set; }

    public Node(string id)
    {
        Id = id;
    }

    public Dictionary<string, Link> OutgoingLinks = new();
    public Dictionary<string, Link> IncomingLinks = new();

    public Link GetOrCreateOutgoingLink(string targetId)
    {
        if (!OutgoingLinks.ContainsKey(targetId))
        {
            OutgoingLinks[targetId] = new Link(targetId);
        }

        return OutgoingLinks[targetId];
    }

    public Link GetOrCreateIncomingLink(string sourceId)
    {
        if (!IncomingLinks.ContainsKey(sourceId))
        {
            IncomingLinks[sourceId] = new Link(sourceId);
        }

        return IncomingLinks[sourceId];
    }
}