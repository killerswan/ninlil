(* Copyright (c) Kevin Cantu <me@kevincantu.org>

   Tumblr - 
      This module provides tools which can be used
      to manipulate existing Tumblr posts,
      (given the blog name, email, and password).
*)

module Tumblr

open System.Collections.Generic
open System.Net
open System.IO
open System.Threading
open System.Text.RegularExpressions
open System.Xml
open System.Drawing


// HTTP utility functions /////////////////////////////////////

// combines key/values into a string with = and &
// for use with HTTP GET and POST
let combine (m: Map<string,string>) : string = 
   Map.fold (fun state key v -> 
                  let next = key + "=" + v
                  match state with
                  | "" ->               next
                  | _  -> state + "&" + next) 
            ""
            m


// HTTP GET
// Note: the point of the async {} is to try not to block so much,
// but in this program it is over-engineering. :D
let httpget (url: string) (data: Map<string,string> ) : string = 
   Async.RunSynchronously(async {
      let url' = url + "?" + (combine data)

      let req        = WebRequest.Create(url', Timeout=15000)
      use! response  = req.AsyncGetResponse()
      use reader     = new StreamReader(response.GetResponseStream())
      let output = reader.ReadToEnd()
      return output
   })


// HTTP POST
let httppost (url: string) (data: Map<string,string>) : string =
   Async.RunSynchronously(async {
      let data' : byte[] = System.Text.Encoding.ASCII.GetBytes(combine data);

      let request = WebRequest.Create(url, Timeout=15000)  // sensitive to too short a delay
      request.Method        <- "POST"
      request.ContentType   <- "application/x-www-form-urlencoded"
      request.ContentLength <- (int64) data'.Length

      use wstream = request.GetRequestStream() 
      wstream.Write(data',0, (data'.Length))
      wstream.Flush()
      wstream.Close()

      use! response = request.AsyncGetResponse()
      use reader    = new StreamReader(response.GetResponseStream())
      let output    = reader.ReadToEnd()

      reader.Close()
      response.Close()
      request.Abort()

      return output
   })


// Tumblr /////////////////////////////////////

// one Tumblr post
// This could be expanded to include more of the properties present
type Post(postxml: XmlNode) =

   // "2010-11-24 05:57:26 GMT"
   let processDate (datestring: string) : System.DateTime =
      (System.DateTime.ParseExact( (datestring.Split [| ' ' |]).[0], "yyyy-MM-dd", null )) 

   member p.XML : XmlNode           = postxml
   member p.id : string             = postxml.Attributes.GetNamedItem("id").Value
   member p.rkey : string           = postxml.Attributes.GetNamedItem("reblog-key").Value
   member p.date : System.DateTime  = postxml.Attributes.GetNamedItem("date-gmt").Value |> processDate
   member p.picURL : string         = postxml.ChildNodes.Item(1).InnerText

   member p.display : string        = 
      sprintf "id: '%s', rkey: '%s', '%s'\n   %s" p.id p.rkey (p.date.ToString()) p.picURL


// API for a given account
type API(blog: string, email: string, password: string) =

   // read via personal Tumblr API
   let readPosts ((start,num): int*int) : string = 
         let url  = "http://" + blog + ".tumblr.com/api/read"
         let data = Map.ofList [ "start", (sprintf "%d" start);
                                 "num",   (sprintf "%d" num);
                                 "type",  "photo" ]

         printfn "-> reading..."
         let xml = httpget url data
         xml


   // delete using Tumblr API
   let deletePost (id: string) : string =
         let url  = "http://www.tumblr.com/api/delete"
         let data = Map.ofList [ "email",    email;
                                 "password", password;
                                 "post-id",  id ]

         printfn "-# deleting..."
         let status = httppost url data
         printfn "   status: '%s'" status

         status
                        

   // reblog using Tumblr API
   // often works even though Tumblr returns an error
   let reblogPost (id: string) (rkey: string) : string =
         let url  = "http://www.tumblr.com/api/reblog"
         let data = Map.ofList [ "email",      email; 
                                 "password",   password; 
                                 "post-id",    id; 
                                 "reblog-key", rkey ]

         printfn "-* reblogging id='%s' rkey='%s'..." id rkey
         let newid = httppost url data
         printfn "   newid: '%s'" newid

         newid


   // process XML results
   let processPosts (postsXML) =
      let doc = XmlDocument()
      postsXML |> doc.LoadXml // so doc is mutable?

      // add prettier printing
      //fsi.AddPrinter( fun (x:XmlNode) -> x.OuterXml );;

      // process the data
      let tumblr = doc.ChildNodes.Item(1)
      let posts  = tumblr.ChildNodes.Item(1)

      // overall statistics
      let start = System.Convert.ToInt32(posts.Attributes.GetNamedItem("start").Value)
      let total = System.Convert.ToInt32(posts.Attributes.GetNamedItem("total").Value)
      let num   = posts.ChildNodes.Count

      // posts.HasChildNodes
      let postsFound = [ for kk in 0..(num-1) do
                         let postxml = posts.ChildNodes.Item(kk)
                         yield Post(postxml) ]

      // display a post tuple
      let display (post: Post) = 
         printfn "   %s" (post.display) |> ignore

      // print stats
      printfn "   read %d to %d of %d" start (start+num-1) total |> ignore

      // print all
      postsFound |> List.map display |> ignore

      (start, total, postsFound)


   // members /////////////////////////////////////
   member tumblr.delete = deletePost
   member tumblr.reblog = reblogPost
   member tumblr.read   = readPosts
   member tumblr.readAndProcess = readPosts >> processPosts

