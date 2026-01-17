using System.Text.Json;

Dictionary<(string,string), int> edges = new();

List<Packet> packets = new();
int i;
for(i = 0; i < args.Length; i++)
{
    if (args[i].StartsWith("--"))
    {
        break;
    }

    packets.AddRange(JsonSerializer.Deserialize<Packet[]>(File.ReadAllText(args[i]))!);
}

if (packets.Count == 0)
{
    Console.WriteLine("No packets found");
    return;
}

foreach(var packet in packets!)
{
    List<string> path = packet.path.ToList();
    path.Add(packet.origin_id.Substring(0,2).ToLowerInvariant());

    if (path.Count < 2)
    {
        continue;
    }

    string sender = path.First();

    foreach(var receiver in path.Skip(1))
    {
        if (sender != receiver)
        {
            var key = (sender, receiver);
            if (edges.ContainsKey(key))
            {
                edges[key]++;
            }
            else
            {
                edges[key] = 1;
            }
        }

        sender = receiver;
    }
}



Dictionary<string, Dictionary<string, int>> neighbors = new();
foreach(var edge in edges)
{
    if (!neighbors.ContainsKey(edge.Key.Item1))
    {
        neighbors.Add(edge.Key.Item1, new Dictionary<string, int>());
    }

    neighbors[edge.Key.Item1].Add(edge.Key.Item2, edge.Value);
}


if (args[i] == "--route")
{
    string start = args[++i];
    string end = args[++i];
    // BFS
    Queue<(string node, List<string> path)> queue = new();
    queue.Enqueue((start, new List<string> { start }));
    HashSet<string> visited = new();
    while (queue.Count > 0)
    {
        var (node, path) = queue.Dequeue();
        if (node == end)
        {
            Console.WriteLine(string.Join(",", path));
            return;
        }
        // Console.WriteLine($"Visiting {node}");

        visited.Add(node);
        if (neighbors.TryGetValue(node, out var neighborList))
        {
            foreach (var neighbor in neighborList.OrderByDescending(n => n.Value).Select(n => n.Key))
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

    Console.WriteLine("No route found");
}
else if (args[i] == "--dgml")
{
    Console.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
    Console.WriteLine("<DirectedGraph Title=\"MC\" xmlns=\"http://schemas.microsoft.com/vs/2009/dgml\">");
    Console.WriteLine("<Nodes>");
    foreach(var node in edges.Keys.SelectMany(e => new[] { e.Item1, e.Item2 }).Distinct())
    {
        Console.WriteLine($"  <Node Id=\"{node}\" Label=\"{node}\" />");
    }
    Console.WriteLine("</Nodes>");
    Console.WriteLine("<Links>");
    foreach(var edge in edges)
    {
        Console.WriteLine($"  <Link Source=\"{edge.Key.Item1}\" Target=\"{edge.Key.Item2}\" Label=\"{edge.Value}\" />");
    }
    Console.WriteLine("</Links>");
    Console.WriteLine("</DirectedGraph>");
}
else
{
    Console.WriteLine($"Don't know how to handle argument {args[0]}");
}




struct Packet
{
    public string[] path {get;set;}
    public string origin_id {get;set;}
}